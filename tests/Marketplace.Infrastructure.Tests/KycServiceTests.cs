using Marketplace.Application.Kyc;
using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services.Kyc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// M2 KYC service tests (EF InMemory). Covers the mock verified flow (KycVerification written + audited),
/// the reject flow, that consent is recorded before proofing, idempotency on terminal states, the
/// HMAC-with-pepper hashing rule (LEGAL #4), and the verified-KYC tier gate.
/// </summary>
public class KycServiceTests
{
    private static KycService Build(MarketplaceDbContext db, IKycProvider provider, KycOptions? options = null)
        => new(db, provider, TestDb.Audit(db), options ?? new KycOptions());

    private static async Task<Guid> NewUserAsync(MarketplaceDbContext db)
    {
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    [Fact]
    public async Task Start_records_consent_and_creates_initiated_verification_with_audit()
    {
        await using var db = TestDb.NewContext();
        var userId = await NewUserAsync(db);
        var svc = Build(db, new MockKycProvider());

        var result = await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Verified));

        Assert.True(result.Succeeded);
        Assert.Equal("MOCK", result.Value!.Provider);
        Assert.StartsWith("MOCK-", result.Value!.ProviderReference);

        var verification = db.KycVerifications.Single(k => k.UserId == userId);
        Assert.Equal((byte)5, verification.KycStatusId);             // INITIATED
        Assert.Equal((byte)KycLevel.Verified, verification.VerificationLevel);

        // LEGAL: a KYC consent record must exist BEFORE proofing.
        var consent = db.ConsentRecords.Single(c => c.UserId == userId);
        Assert.Equal("KYC", consent.ConsentType);
        Assert.True(consent.IsGranted);

        // Audit: consent + initiation transitions written.
        Assert.Contains(db.AuditLogs, a => a.Action == "Kyc.ConsentRecorded");
        Assert.Contains(db.AuditLogs, a => a.Action == "Kyc.Initiated");
    }

    [Fact]
    public async Task Mock_verified_flow_sets_verified_status_expiry_and_audit()
    {
        await using var db = TestDb.NewContext();
        var userId = await NewUserAsync(db);
        var svc = Build(db, new MockKycProvider());
        var start = await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Verified));
        var provider = new MockKycProvider();
        var callback = await provider.CheckStatusAsync(start.Value!.ProviderReference!);

        var result = await svc.HandleCallbackAsync(callback);

        Assert.True(result.Succeeded);
        Assert.Equal("VERIFIED", result.Value!.StatusCode);
        var verification = db.KycVerifications.Include(k => k.SensitiveData).Single(k => k.UserId == userId);
        Assert.Equal((byte)2, verification.KycStatusId);            // VERIFIED
        Assert.NotNull(verification.VerifiedAtUtc);
        Assert.NotNull(verification.ExpiresAtUtc);
        Assert.True(verification.ExpiresAtUtc! > DateTime.UtcNow.AddDays(360));
        Assert.Contains(db.AuditLogs, a => a.Action == "Kyc.Verified");

        // Mock yields a masked name but no national id hash (no raw PII, LEGAL #4).
        Assert.NotNull(verification.SensitiveData);
        Assert.Null(verification.SensitiveData!.NationalIdHash);
        Assert.True(verification.SensitiveData.RetentionExpiresAtUtc > DateTime.UtcNow);

        Assert.True(await svc.IsKycVerifiedAsync(userId)); // tier gate now passes
    }

    [Fact]
    public async Task Reject_flow_sets_rejected_status_and_audit()
    {
        await using var db = TestDb.NewContext();
        var userId = await NewUserAsync(db);
        var svc = Build(db, new MockKycProvider());
        var start = await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Verified));

        var reject = new KycCallback(start.Value!.ProviderReference!, Verified: false, FullNameMasked: null, NationalIdHash: null, RawStatus: "rejected");
        var result = await svc.HandleCallbackAsync(reject);

        Assert.True(result.Succeeded);
        Assert.Equal("REJECTED", result.Value!.StatusCode);
        var verification = db.KycVerifications.Single(k => k.UserId == userId);
        Assert.Equal((byte)3, verification.KycStatusId);           // REJECTED
        Assert.Null(verification.VerifiedAtUtc);
        Assert.Contains(db.AuditLogs, a => a.Action == "Kyc.Rejected");
        Assert.False(await svc.IsKycVerifiedAsync(userId));
    }

    [Fact]
    public async Task HandleCallback_is_idempotent_on_terminal_state()
    {
        await using var db = TestDb.NewContext();
        var userId = await NewUserAsync(db);
        var svc = Build(db, new MockKycProvider());
        var start = await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Premium));
        var callback = await new MockKycProvider().CheckStatusAsync(start.Value!.ProviderReference!);

        await svc.HandleCallbackAsync(callback);
        var verifiedCountAfterFirst = db.AuditLogs.Count(a => a.Action == "Kyc.Verified");
        await svc.HandleCallbackAsync(callback); // repeat — must not re-process / re-audit
        var verifiedCountAfterSecond = db.AuditLogs.Count(a => a.Action == "Kyc.Verified");

        Assert.Equal(verifiedCountAfterFirst, verifiedCountAfterSecond);
    }

    [Fact]
    public async Task Start_rejects_second_inflight_verification()
    {
        await using var db = TestDb.NewContext();
        var userId = await NewUserAsync(db);
        var svc = Build(db, new MockKycProvider());

        await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Verified));
        var second = await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Verified));

        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Verified_flow_with_provider_hash_stores_passthrough_hash()
    {
        // When the provider supplies an already-hashed national id, the service stores it as-is.
        await using var db = TestDb.NewContext();
        var userId = await NewUserAsync(db);
        var options = new KycOptions { NationalIdHashPepper = "test-pepper" };
        var svc = Build(db, new MockKycProvider(), options);
        var start = await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Verified));

        var hash = new byte[] { 1, 2, 3, 4 };
        var callback = new KycCallback(start.Value!.ProviderReference!, Verified: true, FullNameMasked: "X***", NationalIdHash: hash, RawStatus: "completed");
        await svc.HandleCallbackAsync(callback);

        var verification = db.KycVerifications.Include(k => k.SensitiveData).Single(k => k.UserId == userId);
        Assert.Equal(hash, verification.SensitiveData!.NationalIdHash);
    }

    [Fact]
    public void HashNationalId_uses_hmac_with_pepper_and_returns_null_without_pepper()
    {
        using var db = TestDb.NewContext();

        var withPepper = Build(db, new MockKycProvider(), new KycOptions { NationalIdHashPepper = "pepper-A" });
        var hashA = withPepper.HashNationalId("1101700200300");
        var withDifferentPepper = Build(db, new MockKycProvider(), new KycOptions { NationalIdHashPepper = "pepper-B" });
        var hashB = withDifferentPepper.HashNationalId("1101700200300");
        var noPepper = Build(db, new MockKycProvider(), new KycOptions { NationalIdHashPepper = null });

        Assert.NotNull(hashA);
        Assert.Equal(32, hashA!.Length);                 // HMAC-SHA256 = 32 bytes
        Assert.NotEqual(hashA, hashB);                   // pepper changes the digest (keyed hash)
        Assert.Null(noPepper.HashNationalId("1101700200300")); // no pepper => no stored hash (minimal-data)
    }

    [Fact]
    public async Task GetStatus_reports_expired_when_verified_result_past_expiry()
    {
        await using var db = TestDb.NewContext();
        var userId = await NewUserAsync(db);
        var svc = Build(db, new MockKycProvider());
        var start = await svc.StartVerificationAsync(new StartKycRequest(userId, KycLevel.Verified));
        var callback = await new MockKycProvider().CheckStatusAsync(start.Value!.ProviderReference!);
        await svc.HandleCallbackAsync(callback);

        // Force the stored result past its expiry window.
        var verification = db.KycVerifications.Single(k => k.UserId == userId);
        verification.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var status = await svc.GetStatusAsync(userId);
        Assert.Equal("EXPIRED", status.Value!.StatusCode);
        Assert.False(await svc.IsKycVerifiedAsync(userId)); // expired KYC does not satisfy the tier gate
    }
}
