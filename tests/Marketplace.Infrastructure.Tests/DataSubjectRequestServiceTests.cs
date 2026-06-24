using System.Text.Json;
using Marketplace.Application.Privacy;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

public class DataSubjectRequestServiceTests
{
    /// <summary>In-memory export store: keeps the JSON so tests can assert its contents, no disk I/O.</summary>
    private sealed class InMemoryExportStore : IDsarExportStore
    {
        public string? LastJson { get; private set; }
        public Task<string> SaveAsync(Guid userId, Guid requestId, string json, CancellationToken ct = default)
        {
            LastJson = json;
            return Task.FromResult($"mem://{userId:N}/{requestId:N}.json");
        }
    }

    private static (DataSubjectRequestService svc, InMemoryExportStore store) Build(MarketplaceDbContext db)
    {
        var store = new InMemoryExportStore();
        var svc = new DataSubjectRequestService(db, TestDb.Audit(db), store, NullLogger<DataSubjectRequestService>.Instance);
        return (svc, store);
    }

    [Fact]
    public async Task Create_sets_due_in_30_days_and_marks_identity_verified()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var (svc, _) = Build(db);

        var before = DateTime.UtcNow;
        var result = await svc.CreateAsync(new CreateDsarRequest(user.UserId, DsarType.Export));
        var after = DateTime.UtcNow;

        Assert.True(result.Succeeded);
        var entity = db.DataSubjectRequests.Single();
        // DueByUtc = created + 30 days (legal window).
        Assert.InRange(entity.DueByUtc, before.AddDays(30).AddSeconds(-2), after.AddDays(30).AddSeconds(2));
        Assert.NotNull(entity.VerifiedAtUtc);                          // logged-in => identity verified
        Assert.Equal(DataSubjectRequestStatus.Pending, entity.Status); // "Submitted"
        Assert.Contains(db.AuditLogs, a => a.Action == "Dsar.Create");
    }

    [Fact]
    public async Task Export_writes_json_with_own_data_and_excludes_other_parties_and_raw_kyc()
    {
        await using var db = TestDb.NewContext();
        var subject = TestDb.AddUser(db);
        var stranger = TestDb.AddUser(db);
        await db.SaveChangesAsync();

        // Subject's profile.
        db.UserProfiles.Add(new UserProfile { UserId = subject.UserId, DisplayName = "Subject Display", ProvinceCode = "10" });

        // KYC with sensitive raw data that MUST NOT leak into the export.
        var kyc = new KycVerification
        {
            KycVerificationId = Guid.NewGuid(),
            UserId = subject.UserId,
            KycStatusId = 1,
            Provider = "Mock",
            VerificationLevel = 2,
        };
        db.KycVerifications.Add(kyc);
        db.KycSensitiveData.Add(new KycSensitiveData
        {
            KycVerificationId = kyc.KycVerificationId,
            FullNameMasked = "S*** D***",
            NationalIdHash = new byte[] { 1, 2, 3, 4 },
            RetentionExpiresAtUtc = DateTime.UtcNow.AddYears(1),
        });

        // A product listing of the subject + a stranger's product (must not appear).
        var category = await db.Categories.FirstAsync();
        var subjectProduct = new Product { ProductId = Guid.NewGuid(), SellerId = subject.UserId, CategoryId = category.CategoryId, Title = "My Card", FixedPrice = 100m };
        var strangerProduct = new Product { ProductId = Guid.NewGuid(), SellerId = stranger.UserId, CategoryId = category.CategoryId, Title = "Stranger Card", FixedPrice = 200m };
        db.Products.AddRange(subjectProduct, strangerProduct);

        await db.SaveChangesAsync();

        var (svc, store) = Build(db);
        var created = await svc.CreateAsync(new CreateDsarRequest(subject.UserId, DsarType.Export));
        var requestId = created.Value!.DataSubjectRequestId;
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();

        var result = await svc.FulfilExportAsync(requestId, admin.UserId);

        Assert.True(result.Succeeded);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.NotNull(result.Value.ResultArtifactPath);

        var json = store.LastJson!;
        // Own data present.
        Assert.Contains("Subject Display", json);
        Assert.Contains("My Card", json);
        // Other party's data absent.
        Assert.DoesNotContain("Stranger Card", json);
        Assert.DoesNotContain(stranger.Email, json);
        // Raw KYC absent (masked name + national id hash never exported).
        Assert.DoesNotContain("S*** D***", json);
        Assert.DoesNotContain("NationalIdHash", json);

        // Sanity: the package parses and has the expected top-level sections.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var section in new[] { "Subject", "Memberships", "Invoices", "Kyc", "Consents", "Notifications", "Listings", "Bids" })
            Assert.True(root.TryGetProperty(section, out _), $"missing section {section}");
    }

    [Fact]
    public async Task Erasure_anonymizes_user_and_keeps_audit_rows()
    {
        await using var db = TestDb.NewContext();
        var subject = TestDb.AddUser(db);
        subject.PhoneNumber = "0812345678";
        subject.PasswordHash = "hash";
        db.UserProfiles.Add(new UserProfile { UserId = subject.UserId, DisplayName = "Real Name", Bio = "secret" });
        await db.SaveChangesAsync();
        var originalEmail = subject.Email;

        var (svc, _) = Build(db);
        var created = await svc.CreateAsync(new CreateDsarRequest(subject.UserId, DsarType.Erasure));
        var requestId = created.Value!.DataSubjectRequestId;
        var auditCountBefore = db.AuditLogs.Count();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();

        var result = await svc.FulfilErasureAsync(requestId, admin.UserId);

        Assert.True(result.Succeeded);
        Assert.Equal("Completed", result.Value!.Status);

        var user = db.Users.Single(u => u.UserId == subject.UserId);
        Assert.True(user.IsAnonymized);
        Assert.True(user.IsDeleted);
        Assert.NotNull(user.DeletedAtUtc);
        Assert.Null(user.PhoneNumber);
        Assert.Null(user.PasswordHash);
        Assert.NotEqual(originalEmail, user.Email);
        Assert.Contains("deleted-user-", user.Email);

        var profile = db.UserProfiles.Single(p => p.UserId == subject.UserId);
        Assert.DoesNotContain("Real Name", profile.DisplayName);
        Assert.Null(profile.Bio);

        // Audit is APPEND-ONLY: rows only grew; the erasure decision is itself audited.
        Assert.True(db.AuditLogs.Count() > auditCountBefore);
        Assert.Contains(db.AuditLogs, a => a.Action == "Dsar.FulfilErasure");
    }

    [Fact]
    public async Task Export_on_erasure_request_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var subject = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var (svc, _) = Build(db);
        var created = await svc.CreateAsync(new CreateDsarRequest(subject.UserId, DsarType.Erasure));
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();

        var result = await svc.FulfilExportAsync(created.Value!.DataSubjectRequestId, admin.UserId);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetMyRequests_returns_only_callers_requests()
    {
        await using var db = TestDb.NewContext();
        var me = TestDb.AddUser(db);
        var other = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var (svc, _) = Build(db);
        await svc.CreateAsync(new CreateDsarRequest(me.UserId, DsarType.Export));
        await svc.CreateAsync(new CreateDsarRequest(other.UserId, DsarType.Export));

        var mine = await svc.GetMyRequestsAsync(me.UserId);

        Assert.True(mine.Succeeded);
        Assert.Single(mine.Value!);
        Assert.Equal(me.UserId, mine.Value![0].RequestedByUserId);
    }

    [Fact]
    public async Task Reject_closes_request_with_reason_and_audits()
    {
        await using var db = TestDb.NewContext();
        var subject = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var (svc, _) = Build(db);
        var created = await svc.CreateAsync(new CreateDsarRequest(subject.UserId, DsarType.Export));
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();

        var result = await svc.RejectAsync(created.Value!.DataSubjectRequestId, admin.UserId, "duplicate request");

        Assert.True(result.Succeeded);
        Assert.Equal("Rejected", result.Value!.Status);
        Assert.Contains("duplicate request", db.DataSubjectRequests.Single().Note);
        Assert.Contains(db.AuditLogs, a => a.Action == "Dsar.Reject");
    }
}
