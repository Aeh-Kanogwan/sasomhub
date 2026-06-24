using Marketplace.Application.Common;

namespace Marketplace.Application.Disputes;

// M4: dispute flow over an existing no-touch Transaction (FR-21). The platform is a MEDIATOR only —
// it NEVER pays/refunds. Owns dbo.Disputes (Open -> UnderReview -> Resolved/Rejected/Escalated).
// Resolution may feed TrustScore (via ITrustScoreService) and/or BlacklistEntry, using standard
// reason codes (no defamatory free text, Legal #3). Every transition is written to dbo.AuditLogs.

/// <summary>A party to a transaction raises a dispute, citing a standard reason code (FR-21).</summary>
public record RaiseDisputeRequest(Guid TransactionId, Guid RaisedByUserId, int ReasonCodeId, string? Description);

/// <summary>Admin moves a dispute through its lifecycle (UnderReview/Resolved/Rejected/Escalated).</summary>
public record ResolveDisputeRequest(Guid DisputeId, Guid HandledByUserId, string NewStatus, string? Resolution);

public record DisputeDto(
    Guid DisputeId,
    Guid TransactionId,
    Guid RaisedByUserId,
    int? ReasonCodeId,
    string? Description,
    string Status,
    string? Resolution,
    Guid? HandledByUserId,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

/// <summary>M4 dispute mediation service (FR-21). No money movement; transitions are audited.</summary>
public interface IDisputeService
{
    /// <summary>FR-21: raise a dispute on a transaction (status Open). Validates the raiser is a party.</summary>
    Task<Result<DisputeDto>> RaiseAsync(RaiseDisputeRequest request, CancellationToken ct = default);

    /// <summary>Read a single dispute (parties + admin).</summary>
    Task<Result<DisputeDto>> GetAsync(Guid disputeId, CancellationToken ct = default);

    /// <summary>A user's own disputes (raised by them or on their transactions).</summary>
    Task<Result<IReadOnlyList<DisputeDto>>> GetForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Admin queue — disputes by status (NULL = all), oldest-first.</summary>
    Task<Result<IReadOnlyList<DisputeDto>>> GetForAdminAsync(string? status, CancellationToken ct = default);

    /// <summary>
    /// Admin transitions a dispute (UnderReview/Resolved/Rejected/Escalated), records resolution,
    /// and may trigger trust-score / blacklist consequences. Stamps ResolvedAtUtc on terminal states.
    /// </summary>
    Task<Result<DisputeDto>> TransitionAsync(ResolveDisputeRequest request, CancellationToken ct = default);
}
