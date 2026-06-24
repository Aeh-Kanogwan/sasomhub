using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Infrastructure.Migrations
{
    /// <summary>
    /// B-06 / G-6: installs the APPEND-ONLY enforcement triggers that EF Core cannot generate from
    /// the model. Mirrors sql/triggers.sql verbatim (keep in sync). With this migration applied,
    /// <c>dotnet ef database update</c> alone produces a database whose AuditLogs / ConsentRecords /
    /// CreditTransactions / NotificationDeliveryLog tables reject UPDATE/DELETE at the DB layer
    /// (FR-24 / FR-29 / NFR-A1 / LEGAL #8) — no manual schema.sql step is required for the trigger path.
    ///
    /// Idempotent: CREATE OR ALTER, so it is also safe when run on a database that was created from
    /// schema.sql (Option A) and merely needs the migration history stamped. Each CREATE OR ALTER
    /// TRIGGER must be the first statement in its batch, hence one migrationBuilder.Sql(...) per trigger.
    ///
    /// Error code map (asserted by docs/tests): 51001 AuditLogs, 51002 ConsentRecords,
    /// 51003 CreditTransactions, 51004 NotificationDeliveryLog.
    /// </summary>
    public partial class AddAppendOnlyTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_AuditLogs_NoModify ON dbo.AuditLogs
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51001, 'AuditLogs is append-only (FR-24). UPDATE/DELETE is forbidden.', 1;
END;");

            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_ConsentRecords_NoModify ON dbo.ConsentRecords
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51002, 'ConsentRecords is append-only (PDPA). UPDATE/DELETE is forbidden; record a new consent event instead.', 1;
END;");

            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_CreditTransactions_NoModify ON dbo.CreditTransactions
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51003, 'CreditTransactions ledger is append-only (FR-29). UPDATE/DELETE is forbidden; post a correcting Adjustment/Revoke row instead.', 1;
END;");

            migrationBuilder.Sql(@"
CREATE OR ALTER TRIGGER dbo.TR_NotifDelivery_NoModify ON dbo.NotificationDeliveryLog
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51004, 'NotificationDeliveryLog is append-only (LEGAL #8). UPDATE/DELETE is forbidden; append a new attempt row instead.', 1;
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('dbo.TR_NotifDelivery_NoModify') IS NOT NULL DROP TRIGGER dbo.TR_NotifDelivery_NoModify;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.TR_CreditTransactions_NoModify') IS NOT NULL DROP TRIGGER dbo.TR_CreditTransactions_NoModify;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.TR_ConsentRecords_NoModify') IS NOT NULL DROP TRIGGER dbo.TR_ConsentRecords_NoModify;");
            migrationBuilder.Sql("IF OBJECT_ID('dbo.TR_AuditLogs_NoModify') IS NOT NULL DROP TRIGGER dbo.TR_AuditLogs_NoModify;");
        }
    }
}
