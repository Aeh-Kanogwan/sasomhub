using System;
using System.Linq;
using System.Threading.Tasks;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Services;
using Marketplace.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// Worker sweep logic (FR-27/28/29/30/32). We drive each worker's internal RunOnceAsync against the
/// shared InMemory context so the sweep is deterministic (no timers/background loop). These assert the
/// business behaviour: milestone calc, expiry flips, idempotency on re-run, notification delivery.
/// </summary>
public class WorkerServiceTests
{
    private const byte NormalTier = 1;

    private static Membership AddMembership(
        Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, Guid userId,
        MembershipStatus status, DateTime? trialEnd = null, DateTime? paidThrough = null)
    {
        var now = DateTime.UtcNow;
        var m = new Membership
        {
            MembershipId = Guid.NewGuid(),
            UserId = userId,
            MembershipTierId = NormalTier,
            Status = status,
            StartAtUtc = now.AddMonths(-1),
            TrialStartsAtUtc = status == MembershipStatus.Trial ? now.AddMonths(-3) : null,
            TrialEndsAtUtc = trialEnd,
            PaidThroughUtc = paidThrough,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Memberships.Add(m);
        return m;
    }

    // ---------------- MembershipTrialExpiryService ----------------

    [Fact]
    public async Task TrialExpiry_creates_milestone_notice_when_within_threshold()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        // Trial ends in 2 days -> milestones 14 and 3 are crossed (2 <= 14, 2 <= 3) but not 1 (2 > 1).
        AddMembership(db, user.UserId, MembershipStatus.Trial, trialEnd: DateTime.UtcNow.AddDays(2));
        await db.SaveChangesAsync();
        var svc = new MembershipTrialExpiryService(TestDb.WorkerServices(db), NullLogger<MembershipTrialExpiryService>.Instance);

        await svc.RunOnceAsync(default);

        var notices = db.Notifications.Where(n => n.Type == NotificationType.TrialExpiring).ToList();
        Assert.Equal(2, notices.Count);                                  // milestones 14 and 3 crossed
        Assert.Equal(new[] { 3, 14 }, notices.Select(n => n.Milestone!.Value).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(notices, n => n.Milestone == 1);           // 1-day not yet crossed
        Assert.All(notices, n => Assert.Equal(NotificationStatus.Pending, n.Status));
    }

    [Fact]
    public async Task TrialExpiry_is_idempotent_no_duplicate_milestone_notice()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        AddMembership(db, user.UserId, MembershipStatus.Trial, trialEnd: DateTime.UtcNow.AddDays(2));
        await db.SaveChangesAsync();
        var svc = new MembershipTrialExpiryService(TestDb.WorkerServices(db), NullLogger<MembershipTrialExpiryService>.Instance);

        await svc.RunOnceAsync(default);
        await svc.RunOnceAsync(default); // re-run

        Assert.Single(db.Notifications.Where(n => n.Type == NotificationType.TrialExpiring && n.Milestone == 3));
    }

    [Fact]
    public async Task TrialExpiry_flips_lapsed_membership_to_expired_and_audits()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        var m = AddMembership(db, user.UserId, MembershipStatus.Trial, trialEnd: DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        var svc = new MembershipTrialExpiryService(TestDb.WorkerServices(db), NullLogger<MembershipTrialExpiryService>.Instance);

        await svc.RunOnceAsync(default);

        var reloaded = db.Memberships.Single(x => x.MembershipId == m.MembershipId);
        Assert.Equal(MembershipStatus.Expired, reloaded.Status);
        Assert.NotNull(reloaded.EndAtUtc);
        Assert.Contains(db.AuditLogs, a => a.Action == "Membership.Expired");
        Assert.Contains(db.Notifications, n => n.Type == NotificationType.MembershipExpired);
    }

    [Fact]
    public async Task TrialExpiry_expire_flip_is_idempotent()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        AddMembership(db, user.UserId, MembershipStatus.Trial, trialEnd: DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        var svc = new MembershipTrialExpiryService(TestDb.WorkerServices(db), NullLogger<MembershipTrialExpiryService>.Instance);

        await svc.RunOnceAsync(default);
        await svc.RunOnceAsync(default); // re-run must not re-flip / re-audit / re-notify

        Assert.Single(db.AuditLogs.Where(a => a.Action == "Membership.Expired"));
        Assert.Single(db.Notifications.Where(n => n.Type == NotificationType.MembershipExpired));
    }

    // ---------------- NotificationDispatchService ----------------

    [Fact]
    public async Task NotificationDispatch_marks_pending_as_sent_and_is_idempotent()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        db.Notifications.Add(new Notification
        {
            NotificationId = Guid.NewGuid(),
            UserId = user.UserId,
            Type = NotificationType.TrialExpiring,
            Channel = NotificationChannel.Email,
            Milestone = 14,
            ScheduledForUtc = DateTime.UtcNow.AddMinutes(-1),
            Status = NotificationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new NotificationDispatchService(TestDb.WorkerServices(db), NullLogger<NotificationDispatchService>.Instance);

        var firstSent = await svc.RunOnceAsync(default);
        var secondSent = await svc.RunOnceAsync(default);

        Assert.Equal(1, firstSent);
        Assert.Equal(0, secondSent); // already Sent -> not re-delivered
        var notice = db.Notifications.Single();
        Assert.Equal(NotificationStatus.Sent, notice.Status);
        Assert.NotNull(notice.SentAtUtc);
    }

    // ---------------- CreditExpiryService ----------------

    [Fact]
    public async Task CreditExpiry_posts_negative_expiry_row_and_is_idempotent()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var credit = new CreditService(db, TestDb.Audit(db));
        // grant 100 credit that already expired yesterday
        await credit.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:1",
            expiresAtUtc: DateTime.UtcNow.AddDays(-1));
        var svc = new CreditExpiryService(TestDb.WorkerServices(db), NullLogger<CreditExpiryService>.Instance);

        var first = await svc.RunOnceAsync(default);
        var second = await svc.RunOnceAsync(default); // re-run must not double-deduct

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var balance = (await credit.GetBalanceAsync(user.UserId)).Value!.Balance;
        Assert.Equal(0m, balance);
        Assert.Single(db.CreditTransactions.Where(t => t.Type == CreditTransactionType.Expiry));
    }

    [Fact]
    public async Task CreditExpiry_clamps_to_available_balance_when_partly_spent()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var credit = new CreditService(db, TestDb.Audit(db));
        await credit.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:1",
            expiresAtUtc: DateTime.UtcNow.AddDays(-1));
        await credit.SpendAsync(user.UserId, 70m, "Promo:1"); // 30 left
        var svc = new CreditExpiryService(TestDb.WorkerServices(db), NullLogger<CreditExpiryService>.Instance);

        await svc.RunOnceAsync(default);

        // only the remaining 30 is expired (never drives balance < 0)
        Assert.Equal(0m, (await credit.GetBalanceAsync(user.UserId)).Value!.Balance);
        var expiryRow = db.CreditTransactions.Single(t => t.Type == CreditTransactionType.Expiry);
        Assert.Equal(-30m, expiryRow.Amount);
    }

    // ---------------- ListingPromotionExpiryService ----------------

    [Fact]
    public async Task PromotionExpiry_flips_active_lapsed_to_expired_and_is_idempotent()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        // a credit row to satisfy the NOT NULL CreditTransactionId link
        var tx = new CreditTransaction
        {
            UserId = user.UserId, Amount = -10m, Type = CreditTransactionType.PromoSpend,
            RefId = "PromoSpend:x", IdempotencyKey = "PromoSpend:PromoSpend:x", BalanceAfter = 0m,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.CreditTransactions.Add(tx);
        var product = AddProduct(db, user.UserId);
        await db.SaveChangesAsync();
        var promo = new ListingPromotion
        {
            ListingPromotionId = Guid.NewGuid(),
            ProductId = product.ProductId,
            UserId = user.UserId,
            PromotionType = PromotionType.Featured,
            CreditCost = 10m,
            StartsAtUtc = DateTime.UtcNow.AddDays(-5),
            EndsAtUtc = DateTime.UtcNow.AddDays(-1), // lapsed
            Status = ListingPromotionStatus.Active,
            CreditTransactionId = tx.CreditTransactionId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
        };
        db.ListingPromotions.Add(promo);
        await db.SaveChangesAsync();
        var svc = new ListingPromotionExpiryService(TestDb.WorkerServices(db), NullLogger<ListingPromotionExpiryService>.Instance);

        var first = await svc.RunOnceAsync(default);
        var second = await svc.RunOnceAsync(default);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(ListingPromotionStatus.Expired,
            db.ListingPromotions.Single(p => p.ListingPromotionId == promo.ListingPromotionId).Status);
        Assert.Single(db.AuditLogs.Where(a => a.Action == "ListingPromotion.Expired"));
    }

    private static Product AddProduct(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, Guid sellerId)
    {
        var now = DateTime.UtcNow;
        var p = new Product
        {
            ProductId = Guid.NewGuid(),
            SellerId = sellerId,
            CategoryId = 1,
            Title = "T",
            Description = "D",
            ListingType = ListingType.FixedPrice,
            FixedPrice = 100m,
            Status = ProductStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Products.Add(p);
        return p;
    }
}
