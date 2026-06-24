using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.Integration.Tests;

/// <summary>
/// Verifies the INSTEAD OF UPDATE/DELETE append-only triggers actually fire at the SQL
/// layer (FR-24 / FR-29 / PDPA / LEGAL #8). The EF InMemory provider cannot reproduce this.
/// Each test opens its own transaction and rolls back, so no rows survive.
///
/// Triggers under test (sql/schema.sql lines ~934-959):
///   TR_AuditLogs_NoModify        -> THROW 51001
///   TR_ConsentRecords_NoModify   -> THROW 51002
///   TR_CreditTransactions_NoModify -> THROW 51003
///   TR_NotifDelivery_NoModify    -> THROW 51004
/// </summary>
[Collection("SqlServer")]
[Trait("Category", "Integration")]
public sealed class AppendOnlyTriggerTests(SqlServerFixture fx)
{
    private static SqlException AssertThrows(Action act)
    {
        var ex = Assert.Throws<SqlException>(act);
        return ex;
    }

    // ---------- AuditLogs (51001) ----------

    [SkippableFact]
    public void AuditLogs_Update_IsBlocked_51001()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertAuditLog(conn, tx, userId);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"UPDATE dbo.AuditLogs SET Action='tampered' WHERE AuditLogId={id};", tx));
        Assert.Equal(51001, ex.Number);

        tx.Rollback();
    }

    [SkippableFact]
    public void AuditLogs_Delete_IsBlocked_51001()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertAuditLog(conn, tx, userId);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"DELETE FROM dbo.AuditLogs WHERE AuditLogId={id};", tx));
        Assert.Equal(51001, ex.Number);

        tx.Rollback();
    }

    [SkippableFact]
    public void AuditLogs_Insert_IsAllowed()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertAuditLog(conn, tx, userId);
        Assert.True(id > 0); // append is allowed; trigger only intercepts UPDATE/DELETE

        tx.Rollback();
    }

    // ---------- ConsentRecords (51002) ----------

    [SkippableFact]
    public void ConsentRecords_Update_IsBlocked_51002()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertConsent(conn, tx, userId);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"UPDATE dbo.ConsentRecords SET IsGranted=0 WHERE ConsentRecordId={id};", tx));
        Assert.Equal(51002, ex.Number);

        tx.Rollback();
    }

    [SkippableFact]
    public void ConsentRecords_Delete_IsBlocked_51002()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertConsent(conn, tx, userId);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"DELETE FROM dbo.ConsentRecords WHERE ConsentRecordId={id};", tx));
        Assert.Equal(51002, ex.Number);

        tx.Rollback();
    }

    // ---------- CreditTransactions (51003) ----------

    [SkippableFact]
    public void CreditTransactions_Update_IsBlocked_51003()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertCreditTx(conn, tx, userId, amount: 100, balanceAfter: 100, idempotencyKey: null, refId: null);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"UPDATE dbo.CreditTransactions SET Amount=999 WHERE CreditTransactionId={id};", tx));
        Assert.Equal(51003, ex.Number);

        tx.Rollback();
    }

    [SkippableFact]
    public void CreditTransactions_Delete_IsBlocked_51003()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertCreditTx(conn, tx, userId, amount: 100, balanceAfter: 100, idempotencyKey: null, refId: null);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"DELETE FROM dbo.CreditTransactions WHERE CreditTransactionId={id};", tx));
        Assert.Equal(51003, ex.Number);

        tx.Rollback();
    }

    // ---------- NotificationDeliveryLog (51004) ----------

    [SkippableFact]
    public void NotifDelivery_Update_IsBlocked_51004()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertNotifDelivery(conn, tx, userId);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"UPDATE dbo.NotificationDeliveryLog SET Status='Sent' WHERE NotificationDeliveryLogId={id};", tx));
        Assert.Equal(51004, ex.Number);

        tx.Rollback();
    }

    [SkippableFact]
    public void NotifDelivery_Delete_IsBlocked_51004()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var userId = fx.NewUser(conn, tx);
        var id = InsertNotifDelivery(conn, tx, userId);

        var ex = AssertThrows(() => SqlServerFixture.ExecNonQuery(conn,
            $"DELETE FROM dbo.NotificationDeliveryLog WHERE NotificationDeliveryLogId={id};", tx));
        Assert.Equal(51004, ex.Number);

        tx.Rollback();
    }

    // ---------- insert helpers (return identity) ----------

    private static long InsertAuditLog(SqlConnection conn, SqlTransaction tx, Guid actor)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.AuditLogs (ActorUserId, Action, EntityType, EntityId)
                            VALUES (@a,'Created','Test','t1');
                            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        cmd.Parameters.AddWithValue("@a", actor);
        return (long)cmd.ExecuteScalar()!;
    }

    private static long InsertConsent(SqlConnection conn, SqlTransaction tx, Guid user)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.ConsentRecords (UserId, ConsentType, DocumentVersion, IsGranted)
                            VALUES (@u,'Marketing','v1',1);
                            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        cmd.Parameters.AddWithValue("@u", user);
        return (long)cmd.ExecuteScalar()!;
    }

    private static long InsertCreditTx(SqlConnection conn, SqlTransaction tx, Guid user,
        decimal amount, decimal balanceAfter, string? idempotencyKey, string? refId, string type = "Adjustment")
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.CreditTransactions (UserId, Amount, Type, RefId, IdempotencyKey, BalanceAfter)
                            VALUES (@u,@amt,@type,@ref,@idem,@bal);
                            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@amt", amount);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@ref", (object?)refId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@idem", (object?)idempotencyKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bal", balanceAfter);
        return (long)cmd.ExecuteScalar()!;
    }

    private static long InsertNotifDelivery(SqlConnection conn, SqlTransaction tx, Guid user)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.NotificationDeliveryLog
                              (UserId, Provider, Channel, RecipientMasked, TemplateKey, TemplateVersion, RetentionExpiresAtUtc)
                            VALUES (@u,'SmtpDev','Email','a***@x.local','tpl.trial','v1', DATEADD(day,30,SYSUTCDATETIME()));
                            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";
        cmd.Parameters.AddWithValue("@u", user);
        return (long)cmd.ExecuteScalar()!;
    }
}
