using System.Linq;
using Marketplace.Application.Memberships;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

public class MembershipServiceTests
{
    private const byte NormalTier = 1;   // seeded by SeedData: AnnualPriceTHB 1000
    private const byte VerifiedTier = 2; // 1500
    private const byte PremiumTier = 3;  // 2000

    private static MembershipService Build(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db)
        => new(db, new ConfigVersionResolver(db), TestDb.Audit(db));

    [Fact]
    public async Task StartTrial_sets_trial_window_three_months_and_active_gate()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, NormalTier));

        Assert.True(result.Succeeded);
        Assert.Equal(MembershipStatus.Trial, result.Value!.Status);
        Assert.NotNull(result.Value!.TrialEndsAtUtc);
        Assert.True(result.Value!.TrialEndsAtUtc! > DateTime.UtcNow.AddMonths(2)); // ~3 months out
        Assert.True(await svc.IsMembershipActiveAsync(user.UserId));               // gate passes during trial
    }

    [Fact]
    public async Task StartTrial_rejects_second_live_membership()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        await svc.StartTrialAsync(new StartTrialRequest(user.UserId, NormalTier));
        var second = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, NormalTier));

        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Renew_issues_invoice_only_and_does_not_activate_until_confirmed()
    {
        // Flow B: RenewAsync must NOT extend the membership (fixes the renew-before-pay loophole).
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);
        var trial = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, PremiumTier));

        var renew = await svc.RenewAsync(new RenewMembershipRequest(trial.Value!.MembershipId, AutoRenew: true));

        Assert.True(renew.Succeeded);
        var membership = db.Memberships.Single(m => m.MembershipId == trial.Value!.MembershipId);
        Assert.Equal(MembershipStatus.Trial, membership.Status);  // still Trial — not yet paid
        Assert.Null(membership.PaidThroughUtc);                   // not extended
        Assert.Null(membership.PaidAmountTHB);                    // no price snapshot yet
        Assert.True(membership.AutoRenew);                        // preference recorded
        var invoice = db.FeeInvoices.Single(f => f.RelatedMembershipId == membership.MembershipId);
        Assert.Equal(FeeInvoiceStatus.Issued, invoice.Status);    // invoice awaiting payment
        Assert.Equal(FeeType.MembershipRenewal, invoice.FeeType);
        Assert.Equal(2000m, invoice.Amount);                      // lookup fallback price
    }

    [Fact]
    public async Task ConfirmInvoicePaid_activates_renewal_snapshots_price_and_extends_one_year()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);
        var trial = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, PremiumTier));
        await svc.RenewAsync(new RenewMembershipRequest(trial.Value!.MembershipId, AutoRenew: true));
        var invoice = db.FeeInvoices.Single(f => f.RelatedMembershipId == trial.Value!.MembershipId);

        var confirm = await svc.ConfirmInvoicePaidAsync(invoice.FeeInvoiceId);

        Assert.True(confirm.Succeeded);
        var membership = db.Memberships.Single(m => m.MembershipId == trial.Value!.MembershipId);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.NotNull(membership.PaidThroughUtc);
        Assert.True(membership.PaidThroughUtc! > DateTime.UtcNow.AddDays(360)); // ~1 year
        Assert.Equal(2000m, membership.PaidAmountTHB);                          // snapshot = invoice amount
    }

    [Fact]
    public async Task ConfirmInvoicePaid_renewal_is_idempotent()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);
        var trial = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, PremiumTier));
        await svc.RenewAsync(new RenewMembershipRequest(trial.Value!.MembershipId, AutoRenew: true));
        var invoice = db.FeeInvoices.Single(f => f.RelatedMembershipId == trial.Value!.MembershipId);

        await svc.ConfirmInvoicePaidAsync(invoice.FeeInvoiceId);
        var afterFirst = db.Memberships.Single(m => m.MembershipId == trial.Value!.MembershipId).PaidThroughUtc;
        await svc.ConfirmInvoicePaidAsync(invoice.FeeInvoiceId); // repeat — must not extend again
        var afterSecond = db.Memberships.Single(m => m.MembershipId == trial.Value!.MembershipId).PaidThroughUtc;

        Assert.Equal(afterFirst, afterSecond); // no double extension
    }

    [Fact]
    public async Task Renew_invoice_uses_config_price_over_lookup_when_present()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        TestDb.SetConfig(db, ConfigKeys.MembershipAnnualPrice("Premium"), "2500", DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        var svc = Build(db);
        var trial = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, PremiumTier));

        await svc.RenewAsync(new RenewMembershipRequest(trial.Value!.MembershipId, AutoRenew: false));

        var invoice = db.FeeInvoices.Single(f => f.RelatedMembershipId == trial.Value!.MembershipId);
        Assert.Equal(2500m, invoice.Amount); // config price wins, not the 2000 lookup
    }

    [Fact]
    public async Task Upgrade_issues_invoice_and_records_pending_tier_without_switching()
    {
        // Flow B: UpgradeTierAsync must NOT switch the tier until the fee is confirmed paid.
        await using var db = TestDb.NewContext();
        var (svc, membershipId) = await ActiveVerifiedMembershipAsync(db);

        var upgrade = await svc.UpgradeTierAsync(new UpgradeTierRequest(membershipId, PremiumTier)); // 2000

        Assert.True(upgrade.Succeeded);
        var membership = db.Memberships.Single(m => m.MembershipId == membershipId);
        Assert.Equal(VerifiedTier, membership.MembershipTierId);          // tier NOT switched yet
        Assert.Equal(PremiumTier, membership.PendingUpgradeTierId);       // target recorded
        var invoice = db.FeeInvoices.Single(f => f.FeeType == FeeType.MembershipUpgrade);
        Assert.Equal(FeeInvoiceStatus.Issued, invoice.Status);
        Assert.True(invoice.Amount > 0m && invoice.Amount <= 500m);       // pro-rated slice of the 500 diff
    }

    [Fact]
    public async Task ConfirmInvoicePaid_upgrade_switches_tier_and_clears_pending()
    {
        await using var db = TestDb.NewContext();
        var (svc, membershipId) = await ActiveVerifiedMembershipAsync(db);
        await svc.UpgradeTierAsync(new UpgradeTierRequest(membershipId, PremiumTier));
        var invoice = db.FeeInvoices.Single(f => f.FeeType == FeeType.MembershipUpgrade);

        var confirm = await svc.ConfirmInvoicePaidAsync(invoice.FeeInvoiceId);

        Assert.True(confirm.Succeeded);
        var membership = db.Memberships.Single(m => m.MembershipId == membershipId);
        Assert.Equal(PremiumTier, membership.MembershipTierId); // now switched
        Assert.Null(membership.PendingUpgradeTierId);           // pending marker cleared
    }

    /// <summary>Build a Premium-paid Active membership so upgrade preconditions (Status==Active) hold.</summary>
    private static async Task<(MembershipService svc, Guid membershipId)> ActiveVerifiedMembershipAsync(
        Marketplace.Infrastructure.Persistence.MarketplaceDbContext db)
    {
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);
        var trial = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, VerifiedTier)); // 1500
        await svc.RenewAsync(new RenewMembershipRequest(trial.Value!.MembershipId, AutoRenew: false));
        var invoice = await db.FeeInvoices.FirstAsync(f => f.RelatedMembershipId == trial.Value!.MembershipId);
        await svc.ConfirmInvoicePaidAsync(invoice.FeeInvoiceId); // pay so the membership becomes Active
        return (svc, trial.Value!.MembershipId);
    }

    [Fact]
    public async Task IsMembershipActive_false_for_expired_trial()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);
        var trial = await svc.StartTrialAsync(new StartTrialRequest(user.UserId, NormalTier));
        // simulate trial elapsed
        var m = db.Memberships.Single(x => x.MembershipId == trial.Value!.MembershipId);
        m.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        Assert.False(await svc.IsMembershipActiveAsync(user.UserId));
    }
}
