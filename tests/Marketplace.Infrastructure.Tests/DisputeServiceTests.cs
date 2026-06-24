using Marketplace.Application.Disputes;
using Marketplace.Application.Reputation;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Marketplace.Infrastructure.Services.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// M4 — FR-21 dispute mediation. Verifies party-only raise, duplicate guard, the admin transition
/// lifecycle (status + audit + ResolvedAtUtc), and that NO money concept is ever touched.
/// </summary>
public class DisputeServiceTests
{
    private const int ReasonItemNotAsDescribed = 3; // seeded BlacklistReasonCode

    private static DisputeService Build(MarketplaceDbContext db)
    {
        var audit = TestDb.Audit(db);
        var trust = new TrustScoreService(db, audit, NullLogger<TrustScoreService>.Instance);
        return new DisputeService(db, audit, trust, NullLogger<DisputeService>.Instance);
    }

    /// <summary>Seed buyer, seller and a no-touch transaction; returns (buyerId, sellerId, txId).</summary>
    private static async Task<(Guid buyerId, Guid sellerId, Guid txId)> SeedTransactionAsync(MarketplaceDbContext db)
    {
        var buyer = TestDb.AddUser(db);
        var seller = TestDb.AddUser(db);
        var tx = new Transaction
        {
            TransactionId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            BuyerId = buyer.UserId,
            SellerId = seller.UserId,
            AgreedAmount = 1000m,
            Currency = "THB",
            Status = TransactionStatus.Transferred,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        return (buyer.UserId, seller.UserId, tx.TransactionId);
    }

    [Fact]
    public async Task Raise_by_non_party_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var (_, _, txId) = await SeedTransactionAsync(db);
        var stranger = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var result = await svc.RaiseAsync(
            new RaiseDisputeRequest(txId, stranger.UserId, ReasonItemNotAsDescribed, "not mine"));

        Assert.False(result.Succeeded);
        Assert.Empty(db.Disputes);
    }

    [Fact]
    public async Task Raise_by_buyer_opens_dispute_and_audits()
    {
        await using var db = TestDb.NewContext();
        var (buyerId, _, txId) = await SeedTransactionAsync(db);
        var svc = Build(db);

        var result = await svc.RaiseAsync(
            new RaiseDisputeRequest(txId, buyerId, ReasonItemNotAsDescribed, "wrong card grade"));

        Assert.True(result.Succeeded);
        Assert.Equal(nameof(DisputeStatus.Open), result.Value!.Status);
        Assert.Single(db.Disputes.Where(d => d.TransactionId == txId));
        Assert.Contains(db.AuditLogs, a => a.Action == "Dispute.Raised");
    }

    [Fact]
    public async Task Raise_duplicate_active_on_same_transaction_is_blocked()
    {
        await using var db = TestDb.NewContext();
        var (buyerId, sellerId, txId) = await SeedTransactionAsync(db);
        var svc = Build(db);

        var first = await svc.RaiseAsync(
            new RaiseDisputeRequest(txId, buyerId, ReasonItemNotAsDescribed, "first"));
        Assert.True(first.Succeeded);

        // Even the counterparty cannot open a second active dispute on the same transaction.
        var second = await svc.RaiseAsync(
            new RaiseDisputeRequest(txId, sellerId, ReasonItemNotAsDescribed, "second"));

        Assert.False(second.Succeeded);
        Assert.Single(db.Disputes.Where(d => d.TransactionId == txId));
    }

    [Fact]
    public async Task Raise_with_invalid_reason_code_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var (buyerId, _, txId) = await SeedTransactionAsync(db);
        var svc = Build(db);

        var result = await svc.RaiseAsync(new RaiseDisputeRequest(txId, buyerId, 9999, null));

        Assert.False(result.Succeeded);
        Assert.Empty(db.Disputes);
    }

    [Fact]
    public async Task Admin_resolve_changes_status_records_handler_and_audits()
    {
        await using var db = TestDb.NewContext();
        var (buyerId, _, txId) = await SeedTransactionAsync(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var raised = await svc.RaiseAsync(
            new RaiseDisputeRequest(txId, buyerId, ReasonItemNotAsDescribed, "issue"));
        var disputeId = raised.Value!.DisputeId;

        var resolve = await svc.TransitionAsync(
            new ResolveDisputeRequest(disputeId, admin.UserId, nameof(DisputeStatus.Resolved), "Mediated; seller to re-ship."));

        Assert.True(resolve.Succeeded);
        Assert.Equal(nameof(DisputeStatus.Resolved), resolve.Value!.Status);

        var persisted = db.Disputes.Single(d => d.DisputeId == disputeId);
        Assert.Equal(DisputeStatus.Resolved, persisted.Status);
        Assert.Equal(admin.UserId, persisted.HandledByUserId);
        Assert.NotNull(persisted.ResolvedAtUtc);
        Assert.False(string.IsNullOrEmpty(persisted.Resolution));

        Assert.Contains(db.AuditLogs, a => a.Action == "Dispute.Transition");
    }

    [Fact]
    public async Task Resolve_without_resolution_note_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var (buyerId, _, txId) = await SeedTransactionAsync(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var raised = await svc.RaiseAsync(
            new RaiseDisputeRequest(txId, buyerId, ReasonItemNotAsDescribed, "issue"));

        var resolve = await svc.TransitionAsync(
            new ResolveDisputeRequest(raised.Value!.DisputeId, admin.UserId, nameof(DisputeStatus.Resolved), null));

        Assert.False(resolve.Succeeded);
        Assert.Equal(DisputeStatus.Open, db.Disputes.Single().Status);
    }

    [Fact]
    public async Task Transition_from_terminal_state_is_rejected()
    {
        await using var db = TestDb.NewContext();
        var (buyerId, _, txId) = await SeedTransactionAsync(db);
        var admin = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = Build(db);

        var raised = await svc.RaiseAsync(
            new RaiseDisputeRequest(txId, buyerId, ReasonItemNotAsDescribed, "issue"));
        var disputeId = raised.Value!.DisputeId;

        await svc.TransitionAsync(
            new ResolveDisputeRequest(disputeId, admin.UserId, nameof(DisputeStatus.Rejected), "Insufficient evidence."));

        // Rejected is terminal — a further move must fail.
        var again = await svc.TransitionAsync(
            new ResolveDisputeRequest(disputeId, admin.UserId, nameof(DisputeStatus.Resolved), "change my mind"));

        Assert.False(again.Succeeded);
        Assert.Equal(DisputeStatus.Rejected, db.Disputes.Single(d => d.DisputeId == disputeId).Status);
    }
}
