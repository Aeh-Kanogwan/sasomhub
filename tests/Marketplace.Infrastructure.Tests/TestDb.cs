using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// On SQL Server every aggregate root carries a <c>ROWVERSION</c> concurrency token that the
/// database generates. The EF Core InMemory provider does not generate it and enforces the
/// required (non-null) byte[], so every SaveChanges would throw. This interceptor stamps a
/// non-null token on any added/modified entity whose RowVersion is still null — a test-only
/// stand-in for the DB-generated value.
/// </summary>
internal sealed class RowVersionStampInterceptor : SaveChangesInterceptor
{
    private static void Stamp(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var prop = entry.Metadata.FindProperty("RowVersion");
            if (prop is null) continue;
            var member = entry.Property("RowVersion");
            if (member.CurrentValue is null)
                member.CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// In-memory DbContext factory + helpers for the growth/membership service tests.
/// NOTE: the InMemory provider does not honour SQL CHECK constraints, filtered unique
/// indexes or the append-only trigger — those are asserted at the SQL layer / integration
/// tier. These tests cover the C# business logic only (idempotency pre-checks, balances,
/// status transitions, price snapshotting, single-level referral rules).
/// </summary>
internal static class TestDb
{
    public static MarketplaceDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseInMemoryDatabase($"mp-tests-{Guid.NewGuid()}")
            .AddInterceptors(new RowVersionStampInterceptor())
            .EnableSensitiveDataLogging()
            .Options;
        var db = new MarketplaceDbContext(options);
        // Apply HasData lookup seed (MembershipTiers, PromotionPackages, BlacklistReasonCodes, …) into the
        // InMemory store — the provider only materialises HasData seeds on EnsureCreated, and the services
        // under test resolve tiers/reason-codes from these lookups.
        db.Database.EnsureCreated();
        return db;
    }

    public static User AddUser(MarketplaceDbContext db, Guid? id = null)
    {
        var user = new User
        {
            UserId = id ?? Guid.NewGuid(),
            Email = $"u{Guid.NewGuid():N}@test.local",
            NormalizedEmail = "X",
        };
        db.Users.Add(user);
        return user;
    }

    public static ConfigVersion SetConfig(MarketplaceDbContext db, string key, string value, DateTime effectiveFromUtc)
    {
        var cv = new ConfigVersion
        {
            ConfigKey = key,
            Value = value,
            EffectiveFromUtc = effectiveFromUtc,
            CreatedAtUtc = effectiveFromUtc,
        };
        db.ConfigVersions.Add(cv);
        return cv;
    }
}
