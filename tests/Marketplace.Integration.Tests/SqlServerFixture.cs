using Microsoft.Data.SqlClient;
using Xunit;

namespace Marketplace.Integration.Tests;

/// <summary>
/// Resolves the connection string for the dedicated integration test database
/// (MarketplaceDb_IT) and exposes open-connection helpers + tiny seed utilities.
///
/// Connection resolution order:
///   1. MARKETPLACE_CONNECTION_IT  (explicit IT connection — used verbatim)
///   2. MARKETPLACE_CONNECTION     (dev connection — DB name rewritten to
///                                  MarketplaceDb_IT so we NEVER touch dev data)
/// If neither is set, <see cref="Available"/> is false and every test SKIPS.
///
/// The IT database itself is created/maintained out-of-band from sql/schema.sql
/// (see README in the report); the fixture only connects to it.
/// </summary>
public sealed class SqlServerFixture
{
    public const string ItDatabaseName = "MarketplaceDb_IT";

    public string? ConnectionString { get; }
    public bool Available => ConnectionString is not null;
    public string? SkipReason { get; }

    public SqlServerFixture()
    {
        var explicitIt = Environment.GetEnvironmentVariable("MARKETPLACE_CONNECTION_IT");
        var dev = Environment.GetEnvironmentVariable("MARKETPLACE_CONNECTION");

        string? cs = null;
        if (!string.IsNullOrWhiteSpace(explicitIt))
        {
            cs = explicitIt;
        }
        else if (!string.IsNullOrWhiteSpace(dev))
        {
            // Rewrite the catalog so we hit the isolated IT database, never dev's MarketplaceDb.
            var builder = new SqlConnectionStringBuilder(dev) { InitialCatalog = ItDatabaseName };
            cs = builder.ConnectionString;
        }

        if (cs is null)
        {
            SkipReason = "No MARKETPLACE_CONNECTION_IT / MARKETPLACE_CONNECTION env var set — SQL Server integration tests skipped.";
            return;
        }

        // Probe the connection once. If the server/DB is unreachable, skip rather than fail
        // (e.g. a CI box without SQL Server). A genuine logic failure inside a test still fails.
        try
        {
            using var conn = new SqlConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.ExecuteScalar();
            ConnectionString = cs;
        }
        catch (Exception ex)
        {
            SkipReason = $"Cannot reach integration SQL Server ({ItDatabaseName}): {ex.Message}";
        }
    }

    public SqlConnection Open()
    {
        var conn = new SqlConnection(ConnectionString
            ?? throw new InvalidOperationException("SQL Server fixture not available."));
        conn.Open();
        return conn;
    }

    /// <summary>Inserts a throwaway user and returns its UserId (satisfies FKs).</summary>
    public Guid NewUser(SqlConnection conn, SqlTransaction? tx = null)
    {
        var id = Guid.NewGuid();
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO dbo.Users (UserId, Email, NormalizedEmail) VALUES (@id, @e, @ne);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@e", $"it-{id:N}@test.local");
        cmd.Parameters.AddWithValue("@ne", $"IT-{id:N}@TEST.LOCAL");
        cmd.ExecuteNonQuery();
        return id;
    }

    public static int ExecNonQuery(SqlConnection conn, string sql, SqlTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }
}

[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }
