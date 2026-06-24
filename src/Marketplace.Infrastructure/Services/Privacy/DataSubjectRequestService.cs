using System.Text.Json;
using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Privacy;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Privacy;

/// <summary>
/// M3 PDPA DSAR (FR-01/04): a data subject lodges an Export / Erasure / Access / Rectify /
/// WithdrawConsent request about THEIR OWN data; Admin/Support fulfils it. Every transition is
/// mirrored to the append-only <see cref="AuditLog"/> (FR-24).
///
/// IDENTITY-VERIFICATION STATE (PDPA: must verify the requester before fulfilling):
/// DECISION (Tech Lead): keep the 4-state <see cref="DataSubjectRequestStatus"/> {Pending, InProgress,
/// Completed, Rejected}. Identity verification is modelled by the orthogonal <c>VerifiedAtUtc</c>
/// timestamp (set/unset), not a separate enum value, and a fulfilled erasure ends in Completed — so
/// extra IdentityVerified/Erased states would add CHECK-constraint + mapping churn with no behavioural
/// gain. The mapping is therefore:
///   Submitted (logged-in subject)  => Status=Pending,    VerifiedAtUtc set (verified up front)
///   InProgress (admin claimed it)  => Status=InProgress
///   Completed (incl. erasure done) => Status=Completed
///   Rejected                       => Status=Rejected
///
/// EXPORT writes the subject's OWN personal data to a JSON artifact on local storage and stores
/// the path on the row (never the data inline). It excludes counterparties' PII, raw KYC data /
/// NationalIdHash, and internal notes; the download link is meant to expire (TTL, default 7 days).
///
/// ERASURE anonymizes (not hard-deletes): User.IsAnonymized + PII tokenised; append-only rows
/// (AuditLogs / CreditTransactions ledger / FeeInvoices / NotificationDeliveryLog) are KEPT for
/// legal/AML/accounting retention (PDPA s.33 exception) and their reason recorded in the audit.
/// </summary>
public sealed class DataSubjectRequestService : IDataSubjectRequestService
{
    /// <summary>PDPA statutory response window (legal: 30 days) used for <see cref="DataSubjectRequest.DueByUtc"/>.</summary>
    private const int StatutoryWindowDays = 30;

    /// <summary>Export download artifact TTL (days). After this the link is treated as expired.</summary>
    public const int ExportTtlDays = 7;

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MarketplaceDbContext _db;
    private readonly IAuditService _audit;
    private readonly IDsarExportStore _exportStore;
    private readonly ILogger<DataSubjectRequestService> _logger;

    public DataSubjectRequestService(
        MarketplaceDbContext db,
        IAuditService audit,
        IDsarExportStore exportStore,
        ILogger<DataSubjectRequestService> logger)
    {
        _db = db;
        _audit = audit;
        _exportStore = exportStore;
        _logger = logger;
    }

    public async Task<Result<DataSubjectRequestDto>> CreateAsync(CreateDsarRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == request.RequestedByUserId, ct);
        if (user is null)
            return Result<DataSubjectRequestDto>.Fail("Requester not found.");
        if (user.IsDeleted || user.IsAnonymized)
            return Result<DataSubjectRequestDto>.Fail("This account has already been erased.");

        var now = DateTime.UtcNow;
        var entity = new DataSubjectRequest
        {
            DataSubjectRequestId = Guid.NewGuid(),
            RequestType = MapType(request.RequestType),
            Status = DataSubjectRequestStatus.Pending,           // "Submitted"
            RequestedByUserId = request.RequestedByUserId,
            // PDPA identity verification: a logged-in subject is verified up front ("IdentityVerified").
            // VerifiedAtUtc is the verification timestamp the spec requires.
            VerifiedAtUtc = now,
            DueByUtc = now.AddDays(StatutoryWindowDays),         // legal: now + 30 days
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAtUtc = now,
        };
        _db.DataSubjectRequests.Add(entity);

        _audit.Write(
            action: "Dsar.Create",
            entityType: "DataSubjectRequest",
            entityId: entity.DataSubjectRequestId.ToString("N"),
            actorUserId: request.RequestedByUserId,
            after: new { entity.RequestType, Status = entity.Status.ToString(), entity.DueByUtc, IdentityVerifiedAtUtc = entity.VerifiedAtUtc });

        await _db.SaveChangesAsync(ct);
        return Result<DataSubjectRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<IReadOnlyList<DataSubjectRequestDto>>> GetMyRequestsAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _db.DataSubjectRequests.AsNoTracking()
            .Where(r => r.RequestedByUserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        return Result<IReadOnlyList<DataSubjectRequestDto>>.Success(list.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<DataSubjectRequestDto>>> GetForAdminAsync(string? status, CancellationToken ct = default)
    {
        var query = _db.DataSubjectRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DataSubjectRequestStatus>(status, ignoreCase: true, out var parsed))
                return Result<IReadOnlyList<DataSubjectRequestDto>>.Fail($"Unknown status '{status}'.");
            query = query.Where(r => r.Status == parsed);
        }

        var list = await query
            .OrderBy(r => r.DueByUtc)   // oldest-due first (SLA)
            .ToListAsync(ct);
        return Result<IReadOnlyList<DataSubjectRequestDto>>.Success(list.Select(ToDto).ToList());
    }

    public async Task<Result<DataSubjectRequestDto>> AssignAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.DataSubjectRequestId == requestId, ct);
        if (entity is null)
            return Result<DataSubjectRequestDto>.Fail("Request not found.");
        if (entity.Status is DataSubjectRequestStatus.Completed or DataSubjectRequestStatus.Rejected)
            return Result<DataSubjectRequestDto>.Fail("This request is already closed.");
        if (entity.VerifiedAtUtc is null)
            return Result<DataSubjectRequestDto>.Fail("Requester identity is not verified yet.");

        // Idempotent: already InProgress + same handler is a no-op success.
        if (entity.Status == DataSubjectRequestStatus.InProgress && entity.HandledByUserId == handledByUserId)
            return Result<DataSubjectRequestDto>.Success(ToDto(entity));

        var before = Snapshot(entity);
        entity.Status = DataSubjectRequestStatus.InProgress;
        entity.HandledByUserId = handledByUserId;

        _audit.Write("Dsar.Assign", "DataSubjectRequest", entity.DataSubjectRequestId.ToString("N"),
            actorUserId: handledByUserId, before: before, after: Snapshot(entity));

        await _db.SaveChangesAsync(ct);
        return Result<DataSubjectRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<DataSubjectRequestDto>> FulfilExportAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.DataSubjectRequestId == requestId, ct);
        if (entity is null)
            return Result<DataSubjectRequestDto>.Fail("Request not found.");
        if (entity.Status == DataSubjectRequestStatus.Completed)
            return Result<DataSubjectRequestDto>.Success(ToDto(entity)); // idempotent
        if (entity.Status == DataSubjectRequestStatus.Rejected)
            return Result<DataSubjectRequestDto>.Fail("This request was rejected.");
        if (entity.VerifiedAtUtc is null)
            return Result<DataSubjectRequestDto>.Fail("Requester identity is not verified yet.");
        if (entity.RequestType is not (DataSubjectRequestType.Export or DataSubjectRequestType.Access))
            return Result<DataSubjectRequestDto>.Fail("This request is not an Export/Access request.");

        var package = await BuildExportPackageAsync(entity.RequestedByUserId, ct);
        if (package is null)
            return Result<DataSubjectRequestDto>.Fail("Subject user not found.");

        var json = JsonSerializer.Serialize(package, ExportJsonOptions);
        string artifactPath;
        try
        {
            artifactPath = await _exportStore.SaveAsync(entity.RequestedByUserId, entity.DataSubjectRequestId, json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dsar export artifact write failed for request {RequestId}", requestId);
            return Result<DataSubjectRequestDto>.Fail("Failed to write the export artifact.");
        }

        var now = DateTime.UtcNow;
        var before = Snapshot(entity);
        entity.HandledByUserId = handledByUserId;
        entity.ResultArtifactPath = artifactPath;
        entity.Status = DataSubjectRequestStatus.Completed;
        entity.CompletedAtUtc = now;

        _audit.Write("Dsar.FulfilExport", "DataSubjectRequest", entity.DataSubjectRequestId.ToString("N"),
            actorUserId: handledByUserId,
            before: before,
            after: new
            {
                Status = entity.Status.ToString(),
                entity.ResultArtifactPath,
                ExportExpiresAtUtc = now.AddDays(ExportTtlDays),
                Sections = package.IncludedSections,
            });

        await _db.SaveChangesAsync(ct);
        return Result<DataSubjectRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<DataSubjectRequestDto>> FulfilErasureAsync(Guid requestId, Guid handledByUserId, CancellationToken ct = default)
    {
        var useTx = _db.Database.IsRelational();
        await using var tx = useTx
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;

        var entity = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.DataSubjectRequestId == requestId, ct);
        if (entity is null)
            return Result<DataSubjectRequestDto>.Fail("Request not found.");
        if (entity.Status == DataSubjectRequestStatus.Completed)
            return Result<DataSubjectRequestDto>.Success(ToDto(entity)); // idempotent
        if (entity.Status == DataSubjectRequestStatus.Rejected)
            return Result<DataSubjectRequestDto>.Fail("This request was rejected.");
        if (entity.VerifiedAtUtc is null)
            return Result<DataSubjectRequestDto>.Fail("Requester identity is not verified yet.");
        if (entity.RequestType != DataSubjectRequestType.Erasure)
            return Result<DataSubjectRequestDto>.Fail("This request is not an Erasure request.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == entity.RequestedByUserId, ct);
        if (user is null)
            return Result<DataSubjectRequestDto>.Fail("Subject user not found.");

        var now = DateTime.UtcNow;
        var token = $"deleted-user-{user.UserId:N}";

        // --- Anonymize identity (PII tokenised / nulled), NOT hard-deleted ---
        var userBefore = new { user.Email, user.PhoneNumber, user.IsDeleted, user.IsAnonymized };
        if (!user.IsAnonymized)
        {
            user.Email = $"{token}@anonymized.invalid";
            user.NormalizedEmail = $"{token.ToUpperInvariant()}@ANONYMIZED.INVALID";
            user.PhoneNumber = null;
            user.PasswordHash = null;          // revoke any credential
            user.EmailConfirmed = false;
            user.IsAnonymized = true;
            user.IsDeleted = true;
            // PDPA erasure timestamp lives on its own column (distinct from generic soft-delete);
            // DeletedAtUtc is also set so soft-delete filters keep working.
            user.AnonymizedAtUtc = now;
            user.DeletedAtUtc = now;
            user.UpdatedAtUtc = now;
        }

        // Profile display data (name/avatar/bio/province) is direct PII -> tokenise/null.
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.UserId, ct);
        if (profile is not null)
        {
            profile.DisplayName = token;
            profile.AvatarUrl = null;
            profile.Bio = null;
            profile.ProvinceCode = null;
            profile.UpdatedAtUtc = now;
        }

        // KYC sensitive data (masked name + national-id hash + provider ref) -> purge the unnecessary PII,
        // keep the verification status row for fraud/AML lineage.
        var kycSensitive = await _db.KycSensitiveData
            .Where(k => _db.KycVerifications.Any(v => v.KycVerificationId == k.KycVerificationId && v.UserId == user.UserId))
            .ToListAsync(ct);
        foreach (var k in kycSensitive)
        {
            k.FullNameMasked = null;
            k.NationalIdHash = null;
        }

        // --- KEPT for legal retention (PDPA s.33), reason recorded in audit ---
        //  * AuditLogs / CreditTransactions (append-only ledgers, DB no-modify triggers)
        //  * FeeInvoices / PaymentSlips (accounting evidence)
        //  * NotificationDeliveryLog (consumer-protection proof; already masked)
        //  * Transactions / Bids (trade history needed for the OTHER party's records & disputes)
        // We do not touch those rows here.

        var erased = new[] { "User.Email", "User.PhoneNumber", "User.PasswordHash", "UserProfile.*", "KycSensitiveData.FullNameMasked", "KycSensitiveData.NationalIdHash" };
        var keptForLegal = new[] { "AuditLogs", "CreditTransactions", "FeeInvoices", "PaymentSlips", "NotificationDeliveryLog", "Transactions", "Bids" };

        var before = Snapshot(entity);
        entity.HandledByUserId = handledByUserId;
        entity.Status = DataSubjectRequestStatus.Completed;
        entity.CompletedAtUtc = now;

        _audit.Write("Dsar.FulfilErasure", "DataSubjectRequest", entity.DataSubjectRequestId.ToString("N"),
            actorUserId: handledByUserId,
            before: new { Request = before, UserBefore = userBefore },
            after: new
            {
                Status = entity.Status.ToString(),
                AnonymizedUserId = user.UserId,
                ErasedFields = erased,
                KeptForLegalRetention = keptForLegal,
                LegalBasis = "PDPA s.33 — retain accounting/AML/append-only audit records",
            });

        await _db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result<DataSubjectRequestDto>.Success(ToDto(entity));
    }

    public async Task<Result<DataSubjectRequestDto>> RejectAsync(Guid requestId, Guid handledByUserId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result<DataSubjectRequestDto>.Fail("A rejection reason is required.");

        var entity = await _db.DataSubjectRequests.FirstOrDefaultAsync(r => r.DataSubjectRequestId == requestId, ct);
        if (entity is null)
            return Result<DataSubjectRequestDto>.Fail("Request not found.");
        if (entity.Status == DataSubjectRequestStatus.Rejected)
            return Result<DataSubjectRequestDto>.Success(ToDto(entity)); // idempotent
        if (entity.Status == DataSubjectRequestStatus.Completed)
            return Result<DataSubjectRequestDto>.Fail("A completed request cannot be rejected.");

        var before = Snapshot(entity);
        entity.HandledByUserId = handledByUserId;
        entity.Status = DataSubjectRequestStatus.Rejected;
        entity.CompletedAtUtc = DateTime.UtcNow;
        entity.Note = string.IsNullOrWhiteSpace(entity.Note) ? reason.Trim() : $"{entity.Note}\n[Rejected] {reason.Trim()}";

        _audit.Write("Dsar.Reject", "DataSubjectRequest", entity.DataSubjectRequestId.ToString("N"),
            actorUserId: handledByUserId, before: before,
            after: new { Status = entity.Status.ToString(), Reason = reason.Trim() });

        await _db.SaveChangesAsync(ct);
        return Result<DataSubjectRequestDto>.Success(ToDto(entity));
    }

    // ---- Export package assembly (subject's OWN data only) -------------------------------------

    private async Task<DsarExportPackage?> BuildExportPackageAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null) return null;

        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var memberships = await _db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new
            {
                m.MembershipId,
                Tier = m.MembershipTier.Code,
                Status = m.Status.ToString(),
                m.StartAtUtc,
                m.TrialEndsAtUtc,
                m.PaidThroughUtc,
                m.AutoRenew,
            })
            .ToListAsync(ct);

        // Membership/payment: receipt no + amount + date only (company money the user paid).
        var invoices = await _db.FeeInvoices.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.IssuedAtUtc)
            .Select(f => new
            {
                ReceiptNo = f.FeeInvoiceId.ToString("N"),
                FeeType = f.FeeType.ToString(),
                f.Amount,
                f.Currency,
                Status = f.Status.ToString(),
                f.IssuedAtUtc,
                f.PaidAtUtc,
            })
            .ToListAsync(ct);

        // KYC: status/level only — NEVER the raw masked name / NationalIdHash / provider images.
        var kyc = await _db.KycVerifications.AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderBy(k => k.CreatedAtUtc)
            .Select(k => new
            {
                Status = k.KycStatus.Code,
                k.VerificationLevel,
                k.VerifiedAtUtc,
                k.ExpiresAtUtc,
            })
            .ToListAsync(ct);

        var consents = await _db.ConsentRecords.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.ConsentType,
                c.DocumentVersion,
                c.IsGranted,
                c.CreatedAtUtc,
            })
            .ToListAsync(ct);

        // Notification log: masked recipient only (PDPA data-minimisation), no raw payload.
        var notifications = await _db.NotificationDeliveryLogs.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.CreatedAtUtc)
            .Select(n => new
            {
                n.Channel,
                RecipientMasked = n.RecipientMasked,
                n.TemplateKey,
                Status = n.Status.ToString(),
                n.CreatedAtUtc,
                n.SentAtUtc,
            })
            .ToListAsync(ct);

        var listings = await _db.Products.AsNoTracking()
            .Where(p => p.SellerId == userId)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new
            {
                p.ProductId,
                p.Title,
                ListingType = p.ListingType.ToString(),
                Status = p.Status.ToString(),
                p.FixedPrice,
                p.Currency,
                p.CreatedAtUtc,
            })
            .ToListAsync(ct);

        // Bids: the user's OWN bids only; amount + status + time (no other bidders' data).
        var bids = await _db.Bids.AsNoTracking()
            .Where(b => b.BidderId == userId)
            .OrderBy(b => b.PlacedAtUtc)
            .Select(b => new
            {
                b.BidId,
                b.AuctionId,
                b.Amount,
                Status = b.Status.ToString(),
                b.PlacedAtUtc,
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        return new DsarExportPackage
        {
            GeneratedAtUtc = now,
            ExportExpiresAtUtc = now.AddDays(ExportTtlDays),
            Subject = new
            {
                user.UserId,
                user.Email,                 // the subject's OWN email (their data)
                user.PhoneNumber,
                Role = user.Role.ToString(),
                AccountStatus = user.AccountStatus.ToString(),
                user.EmailConfirmed,
                user.CreatedAtUtc,
            },
            Profile = profile is null ? null : new
            {
                profile.DisplayName,
                profile.AvatarUrl,
                profile.Bio,
                profile.ProvinceCode,
            },
            Memberships = memberships,
            Invoices = invoices,
            Kyc = kyc,
            Consents = consents,
            Notifications = notifications,
            Listings = listings,
            Bids = bids,
        };
    }

    // ---- mapping / helpers ---------------------------------------------------------------------

    private static DataSubjectRequestType MapType(DsarType t) => t switch
    {
        DsarType.Export => DataSubjectRequestType.Export,
        DsarType.Erasure => DataSubjectRequestType.Erasure,
        DsarType.Access => DataSubjectRequestType.Access,
        DsarType.Rectify => DataSubjectRequestType.Rectify,
        DsarType.WithdrawConsent => DataSubjectRequestType.WithdrawConsent,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown DSAR type."),
    };

    private static object Snapshot(DataSubjectRequest r) => new
    {
        r.DataSubjectRequestId,
        RequestType = r.RequestType.ToString(),
        Status = r.Status.ToString(),
        r.HandledByUserId,
        r.VerifiedAtUtc,
        r.ResultArtifactPath,
    };

    private static DataSubjectRequestDto ToDto(DataSubjectRequest r) => new(
        r.DataSubjectRequestId,
        r.RequestType.ToString(),
        r.Status.ToString(),
        r.RequestedByUserId,
        r.HandledByUserId,
        r.ResultArtifactPath,
        r.DueByUtc,
        r.CreatedAtUtc,
        r.CompletedAtUtc,
        r.Note);
}

/// <summary>The serializable export package (subject's OWN data only). No counterparty PII, no raw KYC.</summary>
internal sealed class DsarExportPackage
{
    public DateTime GeneratedAtUtc { get; init; }
    public DateTime ExportExpiresAtUtc { get; init; }
    public object Subject { get; init; } = null!;
    public object? Profile { get; init; }
    public object Memberships { get; init; } = null!;
    public object Invoices { get; init; } = null!;
    public object Kyc { get; init; } = null!;
    public object Consents { get; init; } = null!;
    public object Notifications { get; init; } = null!;
    public object Listings { get; init; } = null!;
    public object Bids { get; init; } = null!;

    /// <summary>The section names included (echoed into the audit row for traceability).</summary>
    public string[] IncludedSections => new[]
    {
        "Subject", "Profile", "Memberships", "Invoices", "Kyc", "Consents", "Notifications", "Listings", "Bids",
    };
}
