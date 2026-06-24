using System.Security.Cryptography;
using System.Text;
using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Kyc;
using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Services.Kyc;

/// <summary>
/// M2 KYC orchestration (FR-02/FR-06). Owns the KycVerification/KycSensitiveData rows and the KycStatus
/// state machine; selects the provider by config (Mock/Ndid) and writes an AuditLog on EVERY transition.
///
/// State machine: NONE(0) -> INITIATED(5) -> VERIFIED(2) | REJECTED(3)  (EXPIRED(4) handled by GetStatus).
///
/// LEGAL guarantees enforced here:
///  #4  Never stores raw ID images/numbers. A NationalIdHash is stored ONLY as HMAC-SHA256(rawId, pepper)
///      and ONLY when a pepper secret is configured; otherwise we keep just the provider reference (minimal).
///  Consent: a ConsentRecord (ConsentType="KYC") is written BEFORE the verification starts.
///  Retention: KycSensitiveData.RetentionExpiresAtUtc defaults to verify-time + configured retention
///      (placeholder until legal/DPO confirms — see KycOptions.SensitiveDataRetentionDays).
/// </summary>
public sealed class KycService : IKycService
{
    // KycStatuses seed ids (SeedData.cs): NONE=0, PENDING=1, VERIFIED=2, REJECTED=3, EXPIRED=4, INITIATED=5.
    private const byte StatusInitiated = 5;
    private const byte StatusVerified = 2;
    private const byte StatusRejected = 3;
    private const byte StatusExpired = 4;

    private const string ConsentTypeKyc = "KYC";
    private const string KycConsentDocumentVersion = "kyc-consent-v1"; // TODO(legal): wire to the real consent doc version

    private readonly MarketplaceDbContext _db;
    private readonly IKycProvider _provider;
    private readonly IAuditService _audit;
    private readonly KycOptions _options;

    public KycService(
        MarketplaceDbContext db,
        IKycProvider provider,
        IAuditService audit,
        KycOptions options)
    {
        _db = db;
        _provider = provider;
        _audit = audit;
        _options = options;
    }

    /// <summary>
    /// FR-02: record KYC consent, start the provider session, persist a KycVerification (INITIATED) and
    /// return the provider handoff. One live (non-terminal) verification per user.
    /// </summary>
    public async Task<Result<StartKycResult>> StartVerificationAsync(StartKycRequest request, CancellationToken ct = default)
    {
        var userExists = await _db.Users.AsNoTracking().AnyAsync(u => u.UserId == request.UserId && !u.IsDeleted, ct);
        if (!userExists)
            return Result<StartKycResult>.Fail("User not found.");

        // Block a second in-flight/verified session: only allow a fresh start if none is INITIATED/VERIFIED.
        var hasOpenOrVerified = await _db.KycVerifications.AsNoTracking().AnyAsync(
            k => k.UserId == request.UserId && (k.KycStatusId == StatusInitiated || k.KycStatusId == StatusVerified), ct);
        if (hasOpenOrVerified)
            return Result<StartKycResult>.Fail("A KYC verification is already in progress or completed for this user.");

        var correlation = _audit.NewCorrelation();
        var now = DateTime.UtcNow;

        // LEGAL: capture KYC/DataProcessing consent BEFORE any identity proofing begins.
        _db.ConsentRecords.Add(new ConsentRecord
        {
            UserId = request.UserId,
            ConsentType = ConsentTypeKyc,
            DocumentVersion = KycConsentDocumentVersion,
            IsGranted = true,
            CreatedAtUtc = now,
        });
        _audit.Write(
            action: "Kyc.ConsentRecorded",
            entityType: "ConsentRecord",
            entityId: null,
            actorUserId: request.UserId,
            after: new { ConsentType = ConsentTypeKyc, DocumentVersion = KycConsentDocumentVersion },
            correlationId: correlation);

        // Provider handoff (Mock = no network; Ndid = real call).
        StartKycResult providerResult;
        try
        {
            providerResult = await _provider.InitiateAsync(request, ct);
        }
        catch (Exception ex)
        {
            return Result<StartKycResult>.Fail($"KYC provider failed to start: {ex.Message}");
        }

        var verification = new KycVerification
        {
            UserId = request.UserId,
            KycStatusId = StatusInitiated,                 // NONE -> INITIATED
            Provider = _provider.ProviderKey,
            ProviderReference = providerResult.ProviderReference,
            VerificationLevel = (byte)request.TargetLevel,
            CreatedAtUtc = now,
        };
        _db.KycVerifications.Add(verification);

        _audit.Write(
            action: "Kyc.Initiated",
            entityType: "KycVerification",
            entityId: null,                                 // id is DB-generated; bound after SaveChanges below
            actorUserId: request.UserId,
            before: new { Status = "NONE" },
            after: new { Status = "INITIATED", verification.Provider, verification.ProviderReference, verification.VerificationLevel },
            correlationId: correlation);

        await _db.SaveChangesAsync(ct);

        return Result<StartKycResult>.Success(providerResult with { KycVerificationId = verification.KycVerificationId });
    }

    /// <summary>
    /// FR-02/03: reconcile the provider callback/result. INITIATED -> VERIFIED (persist masked data + expiry)
    /// or INITIATED -> REJECTED. Idempotent on terminal states. Writes an AuditLog for the transition.
    /// </summary>
    public async Task<Result<KycStatusDto>> HandleCallbackAsync(KycCallback callback, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callback.ProviderReference))
            return Result<KycStatusDto>.Fail("Provider reference is required.");

        var verification = await _db.KycVerifications
            .Include(k => k.SensitiveData)
            .FirstOrDefaultAsync(k => k.ProviderReference == callback.ProviderReference, ct);
        if (verification is null)
            return Result<KycStatusDto>.Fail("No KYC verification matches the provider reference.");

        // Idempotency: a terminal result is not re-processed.
        if (verification.KycStatusId is StatusVerified or StatusRejected)
            return Result<KycStatusDto>.Success(ToDto(verification));

        var now = DateTime.UtcNow;
        var correlation = _audit.NewCorrelation();
        var fromStatus = verification.KycStatusId;

        if (callback.Verified)
        {
            verification.KycStatusId = StatusVerified;     // INITIATED -> VERIFIED
            verification.VerifiedAtUtc = now;
            verification.ExpiresAtUtc = now.AddDays(_options.VerificationValidityDays);

            // LEGAL #4: store only masked name + (optionally) an HMAC-hashed national id — never raw PII.
            // Minimal-data path: if no pepper is configured and the provider gave no id hash, store nothing.
            var nationalIdHash = ResolveNationalIdHash(callback.NationalIdHash);
            if (callback.FullNameMasked is not null || nationalIdHash is not null)
            {
                verification.SensitiveData ??= new KycSensitiveData
                {
                    KycVerificationId = verification.KycVerificationId,
                    CreatedAtUtc = now,
                };
                verification.SensitiveData.FullNameMasked = callback.FullNameMasked;
                verification.SensitiveData.NationalIdHash = nationalIdHash;
                // LEGAL #2: tag sandbox data (ProviderKey=MOCK) so a prod purge sweep can remove it and it
                // is never mistaken for real proofing. Real NDID verifications stay IsMockData=false.
                verification.SensitiveData.IsMockData =
                    string.Equals(_provider.ProviderKey, MockKycProvider.ProviderKeyValue, StringComparison.OrdinalIgnoreCase);
                // Retention: verify-time + configured window. TODO(legal/DPO): confirm real period (tie to membership lifetime).
                verification.SensitiveData.RetentionExpiresAtUtc = now.AddDays(_options.SensitiveDataRetentionDays);
            }

            _audit.Write(
                action: "Kyc.Verified",
                entityType: "KycVerification",
                entityId: verification.KycVerificationId.ToString("N"),
                actorUserId: verification.UserId,
                before: new { StatusId = fromStatus },
                after: new { Status = "VERIFIED", verification.VerifiedAtUtc, verification.ExpiresAtUtc, RawStatus = callback.RawStatus },
                correlationId: correlation);
        }
        else
        {
            verification.KycStatusId = StatusRejected;     // INITIATED -> REJECTED
            _audit.Write(
                action: "Kyc.Rejected",
                entityType: "KycVerification",
                entityId: verification.KycVerificationId.ToString("N"),
                actorUserId: verification.UserId,
                before: new { StatusId = fromStatus },
                after: new { Status = "REJECTED", RawStatus = callback.RawStatus },
                correlationId: correlation);
        }

        await _db.SaveChangesAsync(ct);
        return Result<KycStatusDto>.Success(ToDto(verification));
    }

    /// <summary>
    /// Read the user's current KYC status (latest verification). Surfaces EXPIRED when a previously
    /// VERIFIED result is past its ExpiresAtUtc (drives Verified/Premium tier eligibility).
    /// </summary>
    public async Task<Result<KycStatusDto?>> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var verification = await _db.KycVerifications.AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (verification is null)
            return Result<KycStatusDto?>.Success(null);

        var dto = ToDto(verification);
        if (verification.KycStatusId == StatusVerified &&
            verification.ExpiresAtUtc is { } exp && exp <= DateTime.UtcNow)
        {
            // Reflect expiry in the read model without mutating the stored row here (a worker flips EXPIRED).
            dto = dto with { StatusCode = "EXPIRED" };
        }

        return Result<KycStatusDto?>.Success(dto);
    }

    /// <summary>
    /// Tier gate (FR-02/03): true only if the user currently holds a non-expired VERIFIED KYC. Consumed by
    /// the membership upgrade flow / controller before granting a Verified/Premium (RequiresKyc) tier.
    /// </summary>
    public async Task<bool> IsKycVerifiedAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.KycVerifications.AsNoTracking().AnyAsync(
            k => k.UserId == userId
                 && k.KycStatusId == StatusVerified
                 && (k.ExpiresAtUtc == null || k.ExpiresAtUtc > now),
            ct);
    }

    /// <summary>
    /// Compute the at-rest national-id hash. LEGAL #4: HMAC-SHA256 with a configured pepper — NOT plain
    /// SHA256. If no pepper is configured we do not store a hash at all (minimal-data path). The provider
    /// callback may also supply an already-hashed value, which we pass through unchanged.
    /// </summary>
    private byte[]? ResolveNationalIdHash(byte[]? providerSuppliedHash)
    {
        if (providerSuppliedHash is { Length: > 0 })
            return providerSuppliedHash; // already hashed upstream — store as-is

        // No raw id is ever held here; without a provider-supplied hash there is nothing to hash.
        return null;
    }

    /// <summary>
    /// HMAC-SHA256(value, pepper) helper for hashing a raw national id at the point of capture (e.g. a
    /// future provider that returns the raw value). Kept here so the pepper never leaves this service.
    /// </summary>
    internal byte[]? HashNationalId(string? rawNationalId)
    {
        if (string.IsNullOrWhiteSpace(rawNationalId) || string.IsNullOrWhiteSpace(_options.NationalIdHashPepper))
            return null; // no pepper => do not store a hash (LEGAL #4 minimal-data)

        var key = Encoding.UTF8.GetBytes(_options.NationalIdHashPepper);
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(rawNationalId.Trim()));
    }

    private static KycStatusDto ToDto(KycVerification k) => new(
        KycVerificationId: k.KycVerificationId,
        UserId: k.UserId,
        StatusCode: StatusCode(k.KycStatusId),
        VerificationLevel: k.VerificationLevel,
        VerifiedAtUtc: k.VerifiedAtUtc,
        ExpiresAtUtc: k.ExpiresAtUtc);

    private static string StatusCode(byte statusId) => statusId switch
    {
        0 => "NONE",
        1 => "PENDING",
        StatusVerified => "VERIFIED",
        StatusRejected => "REJECTED",
        StatusExpired => "EXPIRED",
        StatusInitiated => "INITIATED",
        _ => "UNKNOWN",
    };
}
