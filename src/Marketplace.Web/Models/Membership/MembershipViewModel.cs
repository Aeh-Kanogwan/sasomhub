using Marketplace.Domain.Enums;

namespace Marketplace.Web.Models.Membership;

/// <summary>
/// FR-05/FR-22/FR-27: membership plans (Normal 1,000 / Verified 1,500 / Premium 2,000 THB/yr),
/// 3-month free trial, auto-renew toggle, advance-notice note (14/3/1 days, FR-32).
/// Plan prices come from MembershipTiers + ConfigVersions (effective price snapshot, FR-35).
/// </summary>
public class MembershipViewModel
{
    public IReadOnlyList<MembershipPlanOption> Plans { get; init; } = new List<MembershipPlanOption>();

    /// <summary>Current membership state for the signed-in user (null for guests).</summary>
    public MembershipStatus? CurrentStatus { get; init; }

    /// <summary>FR-05/FR-27: current membership id — REQUIRED by POST Renew/Upgrade. Empty for guests.
    /// (Added by frontend-dev: the Renew/Upgrade actions take membershipId; the view binds it as a hidden field.)</summary>
    public Guid MembershipId { get; init; }

    public byte? CurrentTierId { get; init; }
    public bool AutoRenew { get; init; }
    public DateTime? TrialEndsAtUtc { get; init; }
    public DateTime? PaidThroughUtc { get; init; }

    /// <summary>Flow B: the user's latest membership invoice (if any) — drives the "pay / pending review" banner.</summary>
    public LatestInvoiceInfo? LatestInvoice { get; init; }
}

public class LatestInvoiceInfo
{
    public Guid FeeInvoiceId { get; init; }
    public decimal Amount { get; init; }
    public FeeInvoiceStatus Status { get; init; }
    /// <summary>True when a Pending payment slip is already awaiting review for this invoice.</summary>
    public bool HasPendingSlip { get; init; }
}

public class MembershipPlanOption
{
    public byte MembershipTierId { get; init; }
    public string Code { get; init; } = string.Empty;          // Normal / Verified / Premium
    public string DisplayName { get; init; } = string.Empty;
    public decimal AnnualPriceTHB { get; init; }
    public bool RequiresKyc { get; init; }
    public bool IsFeatured { get; init; }                       // "POPULAR" highlight (Premium)
    public IReadOnlyList<string> Features { get; init; } = new List<string>();
}
