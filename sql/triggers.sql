/* =============================================================================
   triggers.sql — APPEND-ONLY enforcement triggers (B-06 / G-6)
   -----------------------------------------------------------------------------
   IDEMPOTENT. Safe to run any number of times (CREATE OR ALTER). This is the
   single source of truth for the append-only INSTEAD OF UPDATE,DELETE triggers
   that EF Core migrations CANNOT generate.

   Used by BOTH deploy paths:
     - Option A (deploy.ps1): run after schema.sql to (re)install triggers.
     - Option B (dotnet ef database update): the migration
       20260624080000_AddAppendOnlyTriggers runs this exact SQL via
       migrationBuilder.Sql(...).

   FR-24 / FR-29 / NFR-A1 / LEGAL #8: AuditLogs, ConsentRecords,
   CreditTransactions and NotificationDeliveryLog are IMMUTABLE. Any UPDATE or
   DELETE MUST be rejected by the database itself, so even direct DB access
   cannot tamper with audit / consent / credit-ledger / delivery-evidence
   history. INSERT is intentionally NOT intercepted (append is allowed).

   Error code map (do not change — asserted by integration tests & docs):
     51001 AuditLogs   51002 ConsentRecords   51003 CreditTransactions
     51004 NotificationDeliveryLog
   ========================================================================== */
SET NOCOUNT ON;
GO

CREATE OR ALTER TRIGGER dbo.TR_AuditLogs_NoModify ON dbo.AuditLogs
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51001, 'AuditLogs is append-only (FR-24). UPDATE/DELETE is forbidden.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_ConsentRecords_NoModify ON dbo.ConsentRecords
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51002, 'ConsentRecords is append-only (PDPA). UPDATE/DELETE is forbidden; record a new consent event instead.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_CreditTransactions_NoModify ON dbo.CreditTransactions
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51003, 'CreditTransactions ledger is append-only (FR-29). UPDATE/DELETE is forbidden; post a correcting Adjustment/Revoke row instead.', 1;
END;
GO

-- M1: provider-send evidence is append-only (LEGAL #8). To record a status change, append a NEW row
-- with the same CorrelationId — never UPDATE. NOTE: the PDPA retention-purge worker must run under a
-- principal/role that bypasses this trigger (or temporarily DISABLE it) to physically delete expired rows.
CREATE OR ALTER TRIGGER dbo.TR_NotifDelivery_NoModify ON dbo.NotificationDeliveryLog
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51004, 'NotificationDeliveryLog is append-only (LEGAL #8). UPDATE/DELETE is forbidden; append a new attempt row instead.', 1;
END;
GO

PRINT 'Append-only triggers installed/verified (TR_AuditLogs/ConsentRecords/CreditTransactions/NotifDelivery_NoModify).';
GO
