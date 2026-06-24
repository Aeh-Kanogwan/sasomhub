using Marketplace.Application.Configuration;
using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Marketplace.Infrastructure.Services.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// M7 — FR-31 (B-04/G-4) admin config versioning. Verifies the APPEND-ONLY contract: Set inserts a new
/// ConfigVersion row (never updates an existing one), an EffectiveFromUtc collision is rejected, the
/// resolver returns the latest value effective at now, bad value formats are rejected, and every append
/// writes an audit row. (The DB append-only trigger + unique index are asserted at the SQL tier; these
/// cover the C# logic over the InMemory store.)
/// </summary>
public class ConfigAdminServiceTests
{
    private const string PriceKey = "MembershipTier.Premium.AnnualPriceTHB"; // seeded baseline = 2000

    private static ConfigAdminService Build(MarketplaceDbContext db)
    {
        var audit = TestDb.Audit(db);
        var resolver = new ConfigVersionResolver(db);
        return new ConfigAdminService(db, resolver, audit, NullLogger<ConfigAdminService>.Instance);
    }

    [Fact]
    public async Task Set_appends_a_new_row_and_does_not_update_the_existing_one()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        // The seed already has ONE Premium-price version (id 3, value 2000).
        var before = await db.ConfigVersions.Where(c => c.ConfigKey == PriceKey).ToListAsync();
        Assert.Single(before);
        var originalId = before[0].ConfigVersionId;
        var originalValue = before[0].Value;

        var result = await svc.SetAsync(new SetConfigRequest(
            PriceKey, "2500", DateTime.UtcNow.AddDays(1), admin.UserId, "price bump"));

        Assert.True(result.Succeeded);

        var after = await db.ConfigVersions.Where(c => c.ConfigKey == PriceKey)
            .OrderBy(c => c.ConfigVersionId).ToListAsync();
        // Count INCREASED (append), not replaced.
        Assert.Equal(2, after.Count);
        // The original row is untouched (immutable history).
        Assert.Equal(originalValue, after.Single(c => c.ConfigVersionId == originalId).Value);
        // The new row carries the new value + the admin as author.
        var appended = after.Single(c => c.ConfigVersionId != originalId);
        Assert.Equal("2500", appended.Value);
        Assert.Equal(admin.UserId, appended.CreatedByUserId);
    }

    [Fact]
    public async Task Set_with_colliding_EffectiveFromUtc_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var when = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = await svc.SetAsync(new SetConfigRequest(PriceKey, "2200", when, admin.UserId, "first"));
        Assert.True(first.Succeeded);

        // Same key + same effective instant => collision (mirrors UQ_ConfigVer_KeyEffective).
        var second = await svc.SetAsync(new SetConfigRequest(PriceKey, "2300", when, admin.UserId, "dup"));

        Assert.False(second.Succeeded);
        // Only the seed + the first append exist — the colliding one was NOT inserted.
        Assert.Equal(2, await db.ConfigVersions.CountAsync(c => c.ConfigKey == PriceKey));
    }

    [Fact]
    public async Task Resolver_returns_latest_value_effective_at_now_not_future_versions()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);
        var now = DateTime.UtcNow;

        // A version effective yesterday (should win now) and one effective next year (should not yet apply).
        await svc.SetAsync(new SetConfigRequest(PriceKey, "2400", now.AddDays(-1), admin.UserId, "current"));
        await svc.SetAsync(new SetConfigRequest(PriceKey, "9999", now.AddYears(1), admin.UserId, "future"));

        var resolver = new ConfigVersionResolver(db);
        var effective = await resolver.GetValueAsync(PriceKey, now);
        Assert.Equal("2400", effective);

        // GetCurrentAsync mirrors the resolver (future version not shown as current).
        var current = await svc.GetCurrentAsync();
        Assert.True(current.Succeeded);
        Assert.Equal("2400", current.Value!.Single(c => c.ConfigKey == PriceKey).Value);
    }

    [Fact]
    public async Task Set_with_non_numeric_price_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.SetAsync(new SetConfigRequest(
            PriceKey, "not-a-number", DateTime.UtcNow.AddDays(1), admin.UserId, null));

        Assert.False(result.Succeeded);
        // Unchanged: only the seed row.
        Assert.Single(db.ConfigVersions.Where(c => c.ConfigKey == PriceKey));
    }

    [Fact]
    public async Task Set_with_zero_or_negative_price_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var zero = await svc.SetAsync(new SetConfigRequest(PriceKey, "0", DateTime.UtcNow.AddDays(1), admin.UserId, null));
        var neg = await svc.SetAsync(new SetConfigRequest(PriceKey, "-5", DateTime.UtcNow.AddDays(2), admin.UserId, null));

        Assert.False(zero.Succeeded);
        Assert.False(neg.Succeeded);
        Assert.Single(db.ConfigVersions.Where(c => c.ConfigKey == PriceKey));
    }

    [Fact]
    public async Task Set_with_unknown_key_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.SetAsync(new SetConfigRequest(
            "Totally.Made.Up.Key", "123", DateTime.UtcNow.AddDays(1), admin.UserId, null));

        Assert.False(result.Succeeded);
        Assert.Empty(db.ConfigVersions.Where(c => c.ConfigKey == "Totally.Made.Up.Key"));
    }

    [Fact]
    public async Task Set_writes_an_audit_row_with_before_and_after()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.SetAsync(new SetConfigRequest(
            PriceKey, "2600", DateTime.UtcNow.AddDays(1), admin.UserId, "audited bump"));
        Assert.True(result.Succeeded);

        var audit = db.AuditLogs.Single(a => a.Action == "Config.VersionAppended" && a.EntityId == PriceKey);
        Assert.Equal(admin.UserId, audit.ActorUserId);
        Assert.Equal("ConfigVersion", audit.EntityType);
        // before captured the prior effective value (seed 2000); after carries the new value.
        Assert.NotNull(audit.BeforeJson);
        Assert.Contains("2000", audit.BeforeJson);
        Assert.NotNull(audit.AfterJson);
        Assert.Contains("2600", audit.AfterJson);
    }

    [Fact]
    public async Task GetHistory_returns_all_versions_newest_first()
    {
        await using var db = TestDb.NewContext();
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);
        var now = DateTime.UtcNow;

        await svc.SetAsync(new SetConfigRequest(PriceKey, "2100", now.AddDays(1), admin.UserId, "v2"));
        await svc.SetAsync(new SetConfigRequest(PriceKey, "2200", now.AddDays(2), admin.UserId, "v3"));

        var history = await svc.GetHistoryAsync(PriceKey);
        Assert.True(history.Succeeded);
        var rows = history.Value!;
        Assert.Equal(3, rows.Count); // seed + 2 appends
        // Newest EffectiveFromUtc first.
        Assert.True(rows[0].EffectiveFromUtc >= rows[1].EffectiveFromUtc);
        Assert.True(rows[1].EffectiveFromUtc >= rows[2].EffectiveFromUtc);
        Assert.Equal("2200", rows[0].Value);
    }
}
