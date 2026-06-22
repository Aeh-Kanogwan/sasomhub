namespace Marketplace.Application.Auditing;

// FR-24 / NFR-A1 / NFR-A2: append-only audit trail. Every security/financial/credit/membership
// action writes a row to dbo.AuditLogs {actor, action, entity, before/after JSON, ip-hash,
// correlation-id, timestamp}. Append-only is also enforced at the DB layer
// (TR_AuditLogs_NoModify rejects UPDATE/DELETE) — the app only ever INSERTs.

/// <summary>
/// Append-only audit writer (FR-24). Implementations only ever ADD an <c>AuditLog</c> row to the
/// current DbContext — they do NOT call SaveChanges. The caller persists the audit row in the SAME
/// SaveChanges / transaction as the action being audited, so a money/credit action and its audit row
/// commit (or roll back) together. <see cref="NewCorrelation"/> groups all audit rows of one logical
/// business operation under a single correlation id (NFR-A2).
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Stage an append-only audit row on the current unit of work (NOT saved here — the caller's
    /// SaveChanges commits it together with the audited action). <paramref name="before"/>/<paramref name="after"/>
    /// are serialized to JSON. Returns the correlation id used (a fresh one if none supplied).
    /// </summary>
    Guid Write(
        string action,
        string entityType,
        string? entityId,
        Guid? actorUserId = null,
        object? before = null,
        object? after = null,
        Guid? correlationId = null,
        byte[]? ipAddressHash = null);

    /// <summary>NFR-A2: mint a correlation id to tie together all audit rows of one transaction/flow.</summary>
    Guid NewCorrelation();
}
