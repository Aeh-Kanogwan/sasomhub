using Marketplace.Application.Auditing;
using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// Append-only audit writer (FR-24, NFR-A1/A2). Stages an <see cref="AuditLog"/> row on the current
/// DbContext; it does NOT SaveChanges itself — the caller commits the audit row in the SAME
/// SaveChanges/transaction as the audited action so that, for money/credit operations, the action and
/// its audit either both persist or both roll back (no action without an audit trail).
///
/// append-only = INSERT only. We never UPDATE/DELETE AuditLogs (DB trigger TR_AuditLogs_NoModify also
/// rejects that at the database layer). before/after are serialized to compact JSON.
/// </summary>
public sealed class AuditService : IAuditService
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MarketplaceDbContext _db;

    public AuditService(MarketplaceDbContext db) => _db = db;

    public Guid Write(
        string action,
        string entityType,
        string? entityId,
        Guid? actorUserId = null,
        object? before = null,
        object? after = null,
        Guid? correlationId = null,
        byte[]? ipAddressHash = null)
    {
        var correlation = correlationId ?? Guid.NewGuid();
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,           // null => system/worker
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = Serialize(before),
            AfterJson = Serialize(after),
            IpAddressHash = ipAddressHash,
            CorrelationId = correlation,
            CreatedAtUtc = DateTime.UtcNow,
        });
        return correlation;
    }

    public Guid NewCorrelation() => Guid.NewGuid();

    private static string? Serialize(object? value)
        => value is null ? null : System.Text.Json.JsonSerializer.Serialize(value, JsonOptions);
}
