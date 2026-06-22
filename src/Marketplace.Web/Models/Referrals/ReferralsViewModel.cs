using Marketplace.Domain.Enums;

namespace Marketplace.Web.Models.Referrals;

/// <summary>
/// FR-28 (SINGLE-LEVEL): the user's own referral code + the people they personally invited + credit earned.
///
/// LEGAL INVARIANT: single-level only — this model exposes NO upline/downline/level/tree. It lists exactly
/// the users this caller referred directly, and the non-cashable platform credit granted to the referrer.
/// </summary>
public class ReferralsViewModel
{
    /// <summary>The user's own active referral code for sharing (null => not yet generated; CTA disabled).</summary>
    public string? MyReferralCode { get; init; }

    /// <summary>Total non-cashable credit this user has earned from referrals (referrer side only).</summary>
    public decimal TotalCreditEarned { get; init; }

    /// <summary>Count of invitees whose referral has been rewarded (the qualifying trigger fired).</summary>
    public int RewardedCount { get; init; }

    public IReadOnlyList<ReferralRow> Invited { get; init; } = new List<ReferralRow>();
}

/// <summary>One person this user invited (single-level). Identity is shown as display name only (PDPA-minimal).</summary>
public record ReferralRow(
    string InviteeDisplayName,
    ReferralStatus Status,
    decimal CreditToReferrer,
    DateTime InvitedAtUtc,
    DateTime? RewardedAtUtc);
