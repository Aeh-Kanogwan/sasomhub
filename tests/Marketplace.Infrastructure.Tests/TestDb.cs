using Marketplace.Application.Auditing;
using Marketplace.Application.Credits;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>The real append-only audit writer over the InMemory context (FR-24). Stateless wrapper.</summary>
    public static IAuditService Audit(MarketplaceDbContext db) => new AuditService(db);

    /// <summary>
    /// A minimal IServiceProvider whose scopes all resolve the SAME given DbContext (the InMemory store is
    /// keyed by name, so sharing the instance keeps a worker sweep on the same data the test set up).
    /// Provides the services a worker resolves inside its scope: DbContext, IAuditService, ICreditService.
    /// </summary>
    public static IServiceProvider WorkerServices(MarketplaceDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddScoped<IAuditService>(_ => new AuditService(db));
        services.AddScoped<ICreditService>(_ => new CreditService(db, new AuditService(db)));
        // M1: the real NotificationSender facade over the dev-fallback Log transports (no provider creds
        // needed) so NotificationDispatchService can deliver Email/SMS notices in worker tests and the
        // delivery-log evidence row is persisted exactly as in production.
        var notifOptions = new Marketplace.Infrastructure.Services.Notifications.NotificationOptions();
        services.AddScoped<Marketplace.Application.Notifications.IEmailSender>(
            _ => new Marketplace.Infrastructure.Services.Notifications.LogEmailSender(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Marketplace.Infrastructure.Services.Notifications.LogEmailSender>.Instance));
        services.AddScoped<Marketplace.Application.Notifications.ISmsSender>(
            _ => new Marketplace.Infrastructure.Services.Notifications.LogSmsSender(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Marketplace.Infrastructure.Services.Notifications.LogSmsSender>.Instance));
        services.AddScoped<Marketplace.Application.Notifications.INotificationSender>(sp =>
            new Marketplace.Infrastructure.Services.Notifications.NotificationSender(
                db,
                sp.GetRequiredService<Marketplace.Application.Notifications.IEmailSender>(),
                sp.GetRequiredService<Marketplace.Application.Notifications.ISmsSender>(),
                notifOptions,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Marketplace.Infrastructure.Services.Notifications.NotificationSender>.Instance));
        return services.BuildServiceProvider();
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
