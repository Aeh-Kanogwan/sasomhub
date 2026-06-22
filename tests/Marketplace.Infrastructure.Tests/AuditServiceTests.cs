using System.Linq;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Services;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

public class AuditServiceTests
{
    [Fact]
    public async Task Write_appends_audit_row_with_json_and_correlation()
    {
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);

        var correlation = audit.Write(
            action: "Test.Action",
            entityType: "Thing",
            entityId: "abc",
            actorUserId: user.UserId,
            after: new { X = 1, Y = "z" });
        await db.SaveChangesAsync(); // caller owns the SaveChanges (append-only insert)

        var row = db.AuditLogs.Single();
        Assert.Equal("Test.Action", row.Action);
        Assert.Equal("Thing", row.EntityType);
        Assert.Equal("abc", row.EntityId);
        Assert.Equal(user.UserId, row.ActorUserId);
        Assert.Equal(correlation, row.CorrelationId);
        Assert.Contains("\"X\":1", row.AfterJson);
        Assert.Null(row.BeforeJson); // null inputs are omitted (no empty JSON)
    }

    [Fact]
    public async Task Credit_grant_writes_an_audit_row_in_same_unit_of_work()
    {
        // FR-24/FR-29: a credit grant MUST leave an audit trail, committed together with the ledger row.
        await using var db = TestDb.NewContext();
        var user = TestDb.AddUser(db);
        await db.SaveChangesAsync();
        var svc = new CreditService(db, TestDb.Audit(db));

        await svc.GrantAsync(user.UserId, 100m, CreditTransactionType.ReferralReward, "Referral:99", null);

        var audit = db.AuditLogs.SingleOrDefault(a => a.Action == "Credit.ReferralReward");
        Assert.NotNull(audit);
        Assert.Equal("CreditTransaction", audit!.EntityType);
        Assert.Equal("Referral:99", audit.EntityId);
    }
}
