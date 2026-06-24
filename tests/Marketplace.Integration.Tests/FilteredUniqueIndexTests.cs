using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.Integration.Tests;

/// <summary>
/// Verifies filtered UNIQUE indexes reject duplicates at the SQL layer — and, crucially,
/// that the WHERE filter lets the "allowed" cases (NULL key) through. EF InMemory honours
/// neither the uniqueness nor the filter.
///
/// Indexes under test:
///   UX_CreditTx_Idempotency  (IdempotencyKey) WHERE IdempotencyKey IS NOT NULL   -- B-02 double-credit guard
///   UX_CreditTx_TypeRef      (Type, RefId)     WHERE RefId IS NOT NULL           -- one ledger row per source
///   UX_Notif_NoDup           (UserId,Type,RelatedMembershipId,Milestone) filtered -- B-01 dedupe
///   UQ_ConfigVer_KeyEffective(ConfigKey, EffectiveFromUtc)                        -- B-04 no overlapping versions
///
/// SQL Server raises error 2601 (duplicate key in unique index) / 2627 (unique constraint).
/// </summary>
[Collection("SqlServer")]
[Trait("Category", "Integration")]
public sealed class FilteredUniqueIndexTests(SqlServerFixture fx)
{
    private static void AssertUniqueViolation(Action act)
    {
        var ex = Assert.Throws<SqlException>(act);
        Assert.True(ex.Number is 2601 or 2627,
            $"Expected a unique-violation (2601/2627) but got SQL error {ex.Number}: {ex.Message}");
    }

    // ---------- UX_CreditTx_Idempotency ----------

    [SkippableFact]
    public void CreditTx_DuplicateIdempotencyKey_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);
        InsertCreditTx(conn, tx, user, idempotencyKey: "Referral:ABC", refId: null);

        AssertUniqueViolation(() =>
            InsertCreditTx(conn, tx, user, idempotencyKey: "Referral:ABC", refId: null));

        tx.Rollback();
    }

    [SkippableFact]
    public void CreditTx_NullIdempotencyKey_AllowsManyRows()
    {
        // Filtered WHERE IdempotencyKey IS NOT NULL: NULLs are NOT constrained.
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);
        InsertCreditTx(conn, tx, user, idempotencyKey: null, refId: null);
        InsertCreditTx(conn, tx, user, idempotencyKey: null, refId: null); // must NOT throw
        InsertCreditTx(conn, tx, user, idempotencyKey: null, refId: null);

        tx.Rollback();
    }

    [SkippableFact]
    public void CreditTx_ConcurrentSameIdempotencyKey_OnlyOneSurvives()
    {
        // B-02 race: two committed connections racing the same idempotency key — exactly one
        // wins; the other gets a unique violation. Proves the DB (not just app code) is the
        // final double-credit guard. Uses committed rows on a unique user, then cleans up.
        Skip.IfNot(fx.Available, fx.SkipReason!);

        Guid user;
        using (var setup = fx.Open())
            user = fx.NewUser(setup);

        var key = $"IT:Concurrent:{Guid.NewGuid():N}";
        int success = 0, violations = 0;

        void Attempt()
        {
            try
            {
                using var conn = fx.Open();
                InsertCreditTx(conn, null, user, idempotencyKey: key, refId: null);
                Interlocked.Increment(ref success);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                Interlocked.Increment(ref violations);
            }
        }

        try
        {
            var t1 = Task.Run(Attempt);
            var t2 = Task.Run(Attempt);
            Task.WaitAll(t1, t2);

            Assert.Equal(1, success);
            Assert.Equal(1, violations);

            using var verify = fx.Open();
            using var cmd = verify.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM dbo.CreditTransactions WHERE IdempotencyKey=@k;";
            cmd.Parameters.AddWithValue("@k", key);
            Assert.Equal(1, (int)cmd.ExecuteScalar()!);
        }
        finally
        {
            // Cleanup committed rows: append-only trigger blocks DELETE, so disable it briefly,
            // remove the test rows + user, then re-enable. Scoped to the IT database only.
            using var cleanup = fx.Open();
            SqlServerFixture.ExecNonQuery(cleanup,
                $@"ALTER TABLE dbo.CreditTransactions DISABLE TRIGGER TR_CreditTransactions_NoModify;
                   DELETE FROM dbo.CreditTransactions WHERE UserId='{user}';
                   ALTER TABLE dbo.CreditTransactions ENABLE TRIGGER TR_CreditTransactions_NoModify;
                   DELETE FROM dbo.Users WHERE UserId='{user}';");
        }
    }

    // ---------- UX_CreditTx_TypeRef ----------

    [SkippableFact]
    public void CreditTx_DuplicateTypeRef_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);
        InsertCreditTx(conn, tx, user, idempotencyKey: null, refId: "PROMO-1", type: "PromoSpend");

        AssertUniqueViolation(() =>
            InsertCreditTx(conn, tx, user, idempotencyKey: null, refId: "PROMO-1", type: "PromoSpend"));

        tx.Rollback();
    }

    [SkippableFact]
    public void CreditTx_SameRefDifferentType_IsAllowed()
    {
        // The index is on (Type, RefId): same RefId under a different Type is a different key.
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);
        InsertCreditTx(conn, tx, user, idempotencyKey: null, refId: "X1", type: "PromoSpend");
        InsertCreditTx(conn, tx, user, idempotencyKey: null, refId: "X1", type: "Adjustment"); // must NOT throw

        tx.Rollback();
    }

    // ---------- UX_Notif_NoDup ----------

    [SkippableFact]
    public void Notif_DuplicateMilestone_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);
        var membershipId = InsertMembership(conn, tx, user);

        InsertNotif(conn, tx, user, membershipId, milestone: 14);

        AssertUniqueViolation(() =>
            InsertNotif(conn, tx, user, membershipId, milestone: 14));

        tx.Rollback();
    }

    [SkippableFact]
    public void Notif_NullMembershipOrMilestone_NotConstrained()
    {
        // Filtered WHERE RelatedMembershipId IS NOT NULL AND Milestone IS NOT NULL:
        // non-lifecycle notices (NULL membership/milestone) are not deduped.
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var user = fx.NewUser(conn, tx);
        InsertNotif(conn, tx, user, relatedMembershipId: null, milestone: null);
        InsertNotif(conn, tx, user, relatedMembershipId: null, milestone: null); // must NOT throw

        tx.Rollback();
    }

    // ---------- UQ_ConfigVer_KeyEffective ----------

    [SkippableFact]
    public void ConfigVersion_DuplicateKeyAndEffective_IsRejected()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        // Use a unique key so we never collide with the seeded baseline rows.
        var key = $"IT.Test.{Guid.NewGuid():N}";
        const string effective = "2030-01-01T00:00:00";

        InsertConfigVersion(conn, tx, key, "1", effective);

        AssertUniqueViolation(() =>
            InsertConfigVersion(conn, tx, key, "2", effective));

        tx.Rollback();
    }

    [SkippableFact]
    public void ConfigVersion_SameKeyDifferentEffective_IsAllowed()
    {
        Skip.IfNot(fx.Available, fx.SkipReason!);
        using var conn = fx.Open();
        using var tx = conn.BeginTransaction();

        var key = $"IT.Test.{Guid.NewGuid():N}";
        InsertConfigVersion(conn, tx, key, "1", "2030-01-01T00:00:00");
        InsertConfigVersion(conn, tx, key, "2", "2031-01-01T00:00:00"); // newer version, must NOT throw

        tx.Rollback();
    }

    // ---------- helpers ----------

    private static void InsertCreditTx(SqlConnection conn, SqlTransaction? tx, Guid user,
        string? idempotencyKey, string? refId, string type = "Adjustment")
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.CreditTransactions (UserId, Amount, Type, RefId, IdempotencyKey, BalanceAfter)
                            VALUES (@u,1,@type,@ref,@idem,1);";
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@ref", (object?)refId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@idem", (object?)idempotencyKey ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static Guid InsertMembership(SqlConnection conn, SqlTransaction tx, Guid user)
    {
        var id = Guid.NewGuid();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Tier 1 = Normal (seeded). Status Active to satisfy CK_Membership_Status.
        cmd.CommandText = @"INSERT INTO dbo.Memberships (MembershipId, UserId, MembershipTierId, Status)
                            VALUES (@id,@u,1,'Active');";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", user);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void InsertNotif(SqlConnection conn, SqlTransaction tx, Guid user,
        Guid? relatedMembershipId, int? milestone)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.Notifications (UserId, Type, Channel, RelatedMembershipId, Milestone, ScheduledForUtc)
                            VALUES (@u,'TrialExpiring','Email',@m,@ms, SYSUTCDATETIME());";
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@m", (object?)relatedMembershipId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ms", (object?)milestone ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void InsertConfigVersion(SqlConnection conn, SqlTransaction tx,
        string key, string value, string effectiveFromUtc)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO dbo.ConfigVersions (ConfigKey, Value, EffectiveFromUtc)
                            VALUES (@k,@v,@e);";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.Parameters.AddWithValue("@e", DateTime.Parse(effectiveFromUtc, System.Globalization.CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }
}
