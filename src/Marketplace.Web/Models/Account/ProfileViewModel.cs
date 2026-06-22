using Marketplace.Domain.Enums;
using Marketplace.Web.Models.Catalog;

namespace Marketplace.Web.Models.Account;

/// <summary>
/// FR-16/FR-27/FR-17/FR-15: collector profile (XP/level, Trust Score, membership status,
/// collection gallery, warn-on-deal note, appeal/due-process entry point).
/// </summary>
public class ProfileViewModel
{
    public string DisplayName { get; init; } = string.Empty;
    public bool IsVerified { get; init; }

    // Gamified header
    public int CollectorLevel { get; init; }
    public int CurrentXp { get; init; }
    public int XpForNextLevel { get; init; }

    // Stats
    public int TrustScore { get; init; }
    public int CompletedDeals { get; init; }
    public int CollectionCount { get; init; }

    // Membership (FR-27)
    public MembershipStatus MembershipStatus { get; init; }
    public string MembershipTierLabel { get; init; } = string.Empty;
    public DateTime? TrialEndsAtUtc { get; init; }
    public DateTime? PaidThroughUtc { get; init; }

    public IReadOnlyList<ListingCardViewModel> Collection { get; init; } = new List<ListingCardViewModel>();

    /// <summary>FR-14: append-only Trust Score change log (most-recent first) shown on the profile.</summary>
    public IReadOnlyList<TrustScoreHistoryRow> TrustScoreHistory { get; init; } = new List<TrustScoreHistoryRow>();

    /// <summary>FR-15: true when the user has an open penalty/suspension eligible for appeal.</summary>
    public bool CanAppeal { get; init; }

    /// <summary>FR-15: the blacklist entry to appeal (bound by the appeal form). Empty when nothing is appealable.</summary>
    public Guid AppealableEntryId { get; init; }
}

/// <summary>FR-14: one Trust Score history entry for display (delta + resulting score + reason/time).</summary>
public record TrustScoreHistoryRow(
    DateTime Date,
    int Delta,
    int ScoreAfter,
    string? Reason,
    string? Note);
