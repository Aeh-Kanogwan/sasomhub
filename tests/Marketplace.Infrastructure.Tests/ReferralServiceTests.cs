using Marketplace.Application.Referrals;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Services;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

public class ReferralServiceTests
{
    private static (ReferralService referral, CreditService credit) Build(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db)
    {
        var audit = TestDb.Audit(db);
        var credit = new CreditService(db, audit);
        var config = new ConfigVersionResolver(db);
        return (new ReferralService(db, credit, config, audit), credit);
    }

    [Fact]
    public async Task Register_links_referred_to_referrer()
    {
        await using var db = TestDb.NewContext();
        var referrer = TestDb.AddUser(db);
        var referred = TestDb.AddUser(db);
        db.ReferralCodes.Add(new ReferralCode { UserId = referrer.UserId, Code = "ABC123", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var (svc, _) = Build(db);

        var result = await svc.RegisterAsync(new RegisterReferralRequest(referred.UserId, "ABC123"));

        Assert.True(result.Succeeded);
        Assert.Equal(referrer.UserId, result.Value!.ReferrerUserId);
        Assert.Equal("Pending", result.Value!.Status);
    }

    [Fact]
    public async Task Register_rejects_self_referral()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        db.ReferralCodes.Add(new ReferralCode { UserId = user.UserId, Code = "SELF", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var (svc, _) = Build(db);

        var result = await svc.RegisterAsync(new RegisterReferralRequest(user.UserId, "SELF"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Register_rejects_duplicate_referred_user()
    {
        await using var db = TestDb.NewContext();
        var r1 = TestDb.AddUser(db);
        var r2 = TestDb.AddUser(db);
        var referred = TestDb.AddUser(db);
        db.ReferralCodes.Add(new ReferralCode { UserId = r1.UserId, Code = "C1", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        db.ReferralCodes.Add(new ReferralCode { UserId = r2.UserId, Code = "C2", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var (svc, _) = Build(db);

        await svc.RegisterAsync(new RegisterReferralRequest(referred.UserId, "C1"));
        var second = await svc.RegisterAsync(new RegisterReferralRequest(referred.UserId, "C2"));

        Assert.False(second.Succeeded);   // referred only once (single-level integrity)
    }

    [Fact]
    public async Task QualifyAndReward_pays_both_parties_as_credit_once()
    {
        await using var db = TestDb.NewContext();
        var referrer = TestDb.AddUser(db);
        var referred = TestDb.AddUser(db);
        db.ReferralCodes.Add(new ReferralCode { UserId = referrer.UserId, Code = "ABC123", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        var now = DateTime.UtcNow;
        TestDb.SetConfig(db, ConfigKeys.ReferralRewardReferrer, "50", now.AddDays(-1));
        TestDb.SetConfig(db, ConfigKeys.ReferralRewardReferred, "30", now.AddDays(-1));
        TestDb.SetConfig(db, ConfigKeys.ReferralCreditExpiryDays, "365", now.AddDays(-1));
        await db.SaveChangesAsync();
        var (svc, credit) = Build(db);
        var reg = await svc.RegisterAsync(new RegisterReferralRequest(referred.UserId, "ABC123"));

        var reward1 = await svc.QualifyAndRewardAsync(reg.Value!.ReferralId);
        var reward2 = await svc.QualifyAndRewardAsync(reg.Value!.ReferralId); // idempotent re-run

        Assert.True(reward1.Succeeded);
        Assert.True(reward2.Succeeded);
        Assert.Equal(50m, (await credit.GetBalanceAsync(referrer.UserId)).Value!.Balance); // not 100
        Assert.Equal(30m, (await credit.GetBalanceAsync(referred.UserId)).Value!.Balance);
    }

    [Fact]
    public async Task RevokeForAbuse_claws_back_credit_and_rejects_referral()
    {
        await using var db = TestDb.NewContext();
        var referrer = TestDb.AddUser(db);
        var referred = TestDb.AddUser(db);
        db.ReferralCodes.Add(new ReferralCode { UserId = referrer.UserId, Code = "ABC123", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        var now = DateTime.UtcNow;
        TestDb.SetConfig(db, ConfigKeys.ReferralRewardReferrer, "50", now.AddDays(-1));
        TestDb.SetConfig(db, ConfigKeys.ReferralRewardReferred, "30", now.AddDays(-1));
        await db.SaveChangesAsync();
        var (svc, credit) = Build(db);
        var reg = await svc.RegisterAsync(new RegisterReferralRequest(referred.UserId, "ABC123"));
        await svc.QualifyAndRewardAsync(reg.Value!.ReferralId);

        var revoke = await svc.RevokeForAbuseAsync(reg.Value!.ReferralId, "self-referral ring");

        Assert.True(revoke.Succeeded);
        Assert.Equal(0m, (await credit.GetBalanceAsync(referrer.UserId)).Value!.Balance);
        Assert.Equal(0m, (await credit.GetBalanceAsync(referred.UserId)).Value!.Balance);
        var referral = db.Referrals.Single(r => r.ReferralId == reg.Value!.ReferralId);
        Assert.Equal(ReferralStatus.Rejected, referral.Status);
    }
}
