using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Entities;

// ============================ 4. MEMBERSHIP / SUBSCRIPTION ================

/// <summary>
/// dbo.Memberships — membership lifecycle (FR-27).
/// 3-month free trial from signup; PaidThroughUtc is annual paid expiry.
/// Filtered unique index UX_Membership_LivePerUser enforces one live (Trial/Active) row per user.
/// </summary>
public class Membership
{
    public Guid MembershipId { get; set; }
    public Guid UserId { get; set; }
    public byte MembershipTierId { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Trial;
    public DateTime StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public DateTime? TrialStartsAtUtc { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }   // = TrialStartsAtUtc + 3 months
    public DateTime? PaidThroughUtc { get; set; }   // worker flips Status -> Expired after this
    public bool AutoRenew { get; set; }
    /// <summary>
    /// Y-06/B-04: price snapshot for the paid cycle. Locks the charged amount so later
    /// ConfigVersions price changes never apply retroactively (FR-31). NULL while in Trial.
    /// </summary>
    public decimal? PaidAmountTHB { get; set; }     // CHECK NULL OR >= 0
    /// <summary>
    /// Flow B: tier the user requested to upgrade to, pending payment confirmation. An upgrade
    /// issues a FeeInvoice and records the target here WITHOUT switching tier; the tier only changes
    /// once the fee is confirmed paid (Admin approves the slip), then this is cleared back to NULL.
    /// </summary>
    public byte? PendingUpgradeTierId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public User User { get; set; } = null!;
    public MembershipTier MembershipTier { get; set; } = null!;
}
