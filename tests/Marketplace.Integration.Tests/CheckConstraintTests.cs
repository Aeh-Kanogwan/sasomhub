using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.Integration.Tests;

/// <summary>
/// Verifies SQL CHECK constraints reject out-of-domain values. EF InMemory ignores CHECKs.
/// SQL Server raises error 547 (constraint conflict) on violation.
///
/// Constraints under test:
///   CK_Products_Rarity     Rarity IN ('common','rare','epic','legendary')
///   CK_Products_Status     Status IN ('Draft','Active','Sold','Closed','Removed')
///   CK_ReasonCodes_Severity Severity BETWEEN 1 AND 5
///   CK_Notif_Milestone     Milestone IS NULL OR Milestone IN (14,3,1)
///   CK_CreditTx_Type       Type IN ('ReferralReward','PromoSpend','Adjustment','Expiry','Revoke')
///   CK_CreditAcc_Balance   Balance >= 0
/// </summary>
[Collection("SqlServer")]
[Trait("Category", "Integration")]
public sealed class CheckConstraintTests(SqlServerFixture fx)
{
    private static void AssertCheckViolation(Action act)
    {
        var ex = Assert.Throws<SqlException>(act);
        Assert.Equal(547, ex.Number); // The ... statement conflicted with the CHECK constraint
    }

    private static int InsertCategory(SqlConnection conn, SqlTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.Categories (Name, Slug) VALUES (N'IT Cat', @slug);
                            SELECT CAST(SCOPE_IDENTITY() AS INT);";
        cmd.Parameters.AddWithValue("@slug", $"it-{Guid.NewGuid():N}");
        return (int)cmd.ExecuteScalar()!;
    }

    private static void InsertProduct(SqlConnection conn, SqlTransaction tx, Guid seller, int categoryId,
        string rarity = "common", string status = "Draft")
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.Products (SellerId, CategoryId, Title, Rarity, Status)
                            VALUES (@s,@c,N'IT Product',@r,@st);";
        cmd.Parameters.AddWithValue("@s", seller);
        cmd.Parameters.AddWithValue("@c", categoryId);
        cmd.Parameters.AddWithValue("@r", rarity);
        cmd.Parameters.AddWithValue("@st", status);
        cmd.ExecuteNonQuery();
    }

    // ---------- CK_Products_Rarity ----------

    [SkippableFact]
    public void Product_InvalidRarity_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var seller = fx.NewUser(conn, tx);
        var cat = InsertCategory(conn, tx);

        AssertCheckViolation(() => InsertProduct(conn, tx, seller, cat, rarity: "mythic")); // not in enum

        tx.Rollback();
    }

    [SkippableFact]
    public void Product_ValidRarity_IsAccepted()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var seller = fx.NewUser(conn, tx);
        var cat = InsertCategory(conn, tx);
        InsertProduct(conn, tx, seller, cat, rarity: "legendary"); // must NOT throw

        tx.Rollback();
    }

    // ---------- CK_Products_Status ----------

    [SkippableFact]
    public void Product_InvalidStatus_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var seller = fx.NewUser(conn, tx);
        var cat = InsertCategory(conn, tx);

        AssertCheckViolation(() => InsertProduct(conn, tx, seller, cat, status: "Frozen")); // not in enum

        tx.Rollback();
    }

    // ---------- CK_ReasonCodes_Severity ----------

    [SkippableFact]
    public void ReasonCode_SeverityOutOfRange_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        // Severity 6 is outside 1..5
        AssertCheckViolation(() =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO dbo.BlacklistReasonCodes (ReasonCodeId, Code, DisplayName, Severity)
                                VALUES (99,'IT_BAD',N'IT bad',6);";
            cmd.ExecuteNonQuery();
        });

        tx.Rollback();
    }

    // ---------- CK_Notif_Milestone ----------

    [SkippableFact]
    public void Notif_InvalidMilestone_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);

        // Milestone 7 is not in (14,3,1)
        AssertCheckViolation(() =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO dbo.Notifications (UserId, Type, Channel, Milestone, ScheduledForUtc)
                                VALUES (@u,'TrialExpiring','Email',7, SYSUTCDATETIME());";
            cmd.Parameters.AddWithValue("@u", user);
            cmd.ExecuteNonQuery();
        });

        tx.Rollback();
    }

    // ---------- CK_CreditTx_Type ----------

    [SkippableFact]
    public void CreditTx_InvalidType_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);

        // 'Withdraw' is deliberately not a valid type (non-cashable credit)
        AssertCheckViolation(() =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO dbo.CreditTransactions (UserId, Amount, Type, BalanceAfter)
                                VALUES (@u,-50,'Withdraw',0);";
            cmd.Parameters.AddWithValue("@u", user);
            cmd.ExecuteNonQuery();
        });

        tx.Rollback();
    }

    // ---------- CK_CreditAcc_Balance / CK_CreditTx_BalanceAfter ----------

    [SkippableFact]
    public void CreditTx_NegativeBalanceAfter_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);

        // BalanceAfter must be >= 0 (CK_CreditTx_BalanceAfter) — ledger can never go negative.
        AssertCheckViolation(() =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO dbo.CreditTransactions (UserId, Amount, Type, BalanceAfter)
                                VALUES (@u,-50,'PromoSpend',-10);";
            cmd.Parameters.AddWithValue("@u", user);
            cmd.ExecuteNonQuery();
        });

        tx.Rollback();
    }

    [SkippableFact]
    public void CreditAccount_NegativeBalance_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);

        AssertCheckViolation(() =>
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO dbo.CreditAccounts (UserId, Balance) VALUES (@u,-1);";
            cmd.Parameters.AddWithValue("@u", user);
            cmd.ExecuteNonQuery();
        });

        tx.Rollback();
    }
}
