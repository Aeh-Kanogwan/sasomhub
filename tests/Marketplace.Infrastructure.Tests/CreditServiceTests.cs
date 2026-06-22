using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Services;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

public class CreditServiceTests
{
    [Fact]
    public async Task Grant_increases_balance_and_writes_ledger_row()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = new CreditService(db, TestDb.Audit(db));

        var result = await svc.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:1", null);

        Assert.True(result.Succeeded);
        var balance = await svc.GetBalanceAsync(user.UserId);
        Assert.Equal(100m, balance.Value!.Balance);
        var ledger = await svc.GetLedgerAsync(user.UserId);
        Assert.Single(ledger.Value!);
        Assert.Equal(100m, ledger.Value![0].BalanceAfter);
    }

    [Fact]
    public async Task Grant_is_idempotent_on_same_refId()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = new CreditService(db, TestDb.Audit(db));

        await svc.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:1", null);
        var second = await svc.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:1", null);

        Assert.True(second.Succeeded);                       // retry succeeds as no-op
        var balance = await svc.GetBalanceAsync(user.UserId);
        Assert.Equal(100m, balance.Value!.Balance);          // NOT 200 — no double credit
        var ledger = await svc.GetLedgerAsync(user.UserId);
        Assert.Single(ledger.Value!);
    }

    [Fact]
    public async Task Spend_decreases_balance_and_is_idempotent()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = new CreditService(db, TestDb.Audit(db));
        await svc.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:1", null);

        var spend1 = await svc.SpendAsync(user.UserId, 30m, "Promo:abc");
        var spend2 = await svc.SpendAsync(user.UserId, 30m, "Promo:abc"); // same refId -> idempotent

        Assert.True(spend1.Succeeded);
        Assert.True(spend2.Succeeded);
        var balance = await svc.GetBalanceAsync(user.UserId);
        Assert.Equal(70m, balance.Value!.Balance);          // debited once only
    }

    [Fact]
    public async Task Spend_rejected_when_insufficient_balance()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = new CreditService(db, TestDb.Audit(db));
        await svc.GrantAsync(user.UserId, 10m, CreditTransactionType.ReferralReward, "Referral:1", null);

        var spend = await svc.SpendAsync(user.UserId, 50m, "Promo:abc");

        Assert.False(spend.Succeeded);
        var balance = await svc.GetBalanceAsync(user.UserId);
        Assert.Equal(10m, balance.Value!.Balance);          // unchanged
    }

    [Fact]
    public async Task Expire_reduces_balance_with_negative_row()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = new CreditService(db, TestDb.Audit(db));
        await svc.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:1", null);

        var expire = await svc.ExpireAsync(user.UserId, 40m, "ExpiryBatch:2026-06-17");

        Assert.True(expire.Succeeded);
        var balance = await svc.GetBalanceAsync(user.UserId);
        Assert.Equal(60m, balance.Value!.Balance);
    }

    [Fact]
    public async Task Grant_rejects_non_grant_types_and_non_positive_amount()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = new CreditService(db, TestDb.Audit(db));

        Assert.False((await svc.GrantAsync(user.UserId, 10m, CreditTransactionType.PromoSpend, "x", null)).Succeeded);
        Assert.False((await svc.GrantAsync(user.UserId, 0m, CreditTransactionType.ReferralReward, "x", null)).Succeeded);
    }
}
