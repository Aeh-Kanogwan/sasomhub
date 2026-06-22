using Marketplace.Application.Common;

namespace Marketplace.Application.Reputation;

// FR-13..FR-16: Trust Score. Start 100, append-only history, auto-suspend < 60,
// severe offenses PROPOSE Ban + internal Blacklist but require Admin confirmation
// (DP-3 due process, human-in-the-loop — never auto-permaban).

/// <summary>Apply a scored event to a user (FR-14). Delta is taken from the reason code unless overridden.</summary>
public record ApplyTrustEventRequest(
    Guid UserId,
    int ReasonCodeId,
    Guid? RelatedTransactionId = null,
    int? DeltaOverride = null,
    Guid? ActorUserId = null,
    string? Note = null);

public record TrustScoreDto(Guid UserId, int Score, DateTime LastCalculatedAtUtc);

public record TrustScoreHistoryDto(
    long TrustScoreHistoryId,
    int Delta,
    int ScoreAfter,
    int? ReasonCodeId,
    Guid? RelatedTransactionId,
    string? Note,
    DateTime CreatedAtUtc);

/// <summary>Trust-score service (FR-13..FR-16). History is append-only; severe cases need Admin confirmation.</summary>
public interface ITrustScoreService
{
    /// <summary>FR-13: ensure a user has a TrustScore row (=100) with an initial history entry.</summary>
    Task<Result<TrustScoreDto>> EnsureInitializedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>FR-14/FR-15: apply a scored event, append history, and run auto-penalty rules.</summary>
    Task<Result<TrustScoreDto>> ApplyEventAsync(ApplyTrustEventRequest request, CancellationToken ct = default);

    /// <summary>FR-16: read the user's own score history (append-only).</summary>
    Task<Result<IReadOnlyList<TrustScoreHistoryDto>>> GetHistoryAsync(Guid userId, CancellationToken ct = default);

    /// <summary>FR-14/FR-15: worker entry point — recompute &amp; re-apply penalty status for one user.</summary>
    Task<Result> RecalculateAsync(Guid userId, CancellationToken ct = default);
}
