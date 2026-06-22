using Marketplace.Application.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Memberships;

// FR-27: annual membership (Normal 1000 / Verified 1500 / Premium 2000), 3-month free trial,
// renew/auto-renew, advance reminders 14/3/1 days. Membership must be Active/Trial to bid/list
// (FR-06/FR-10). All fees billed via FeeInvoices — separate from buyer<->seller money (no-touch).

public record StartTrialRequest(Guid UserId, byte MembershipTierId);
public record RenewMembershipRequest(Guid MembershipId, bool AutoRenew);
public record UpgradeTierRequest(Guid MembershipId, byte NewTierId);

public record MembershipDto(
    Guid MembershipId,
    Guid UserId,
    byte MembershipTierId,
    MembershipStatus Status,
    DateTime? TrialEndsAtUtc,
    DateTime? PaidThroughUtc,
    bool AutoRenew);

/// <summary>Membership lifecycle service (FR-05, FR-27). Skeleton — no business logic yet.</summary>
public interface IMembershipService
{
    /// <summary>FR-27: create TRIAL membership = signup, trialEnd = signup + 3 months; disclose price/expiry up front.</summary>
    Task<Result<MembershipDto>> StartTrialAsync(StartTrialRequest request, CancellationToken ct = default);

    /// <summary>
    /// FR-27: request a renewal — issues a FeeInvoice (Issued) ONLY. The membership is NOT activated/extended
    /// until the fee is confirmed paid (Flow B: slip upload + Admin approval), via <see cref="ConfirmInvoicePaidAsync"/>.
    /// </summary>
    Task<Result<MembershipDto>> RenewAsync(RenewMembershipRequest request, CancellationToken ct = default);

    /// <summary>
    /// FR-05: request a tier upgrade — issues a pro-rated FeeInvoice (Issued) ONLY. The tier change is NOT
    /// applied until the fee is confirmed paid (Flow B), via <see cref="ConfirmInvoicePaidAsync"/>.
    /// </summary>
    Task<Result<MembershipDto>> UpgradeTierAsync(UpgradeTierRequest request, CancellationToken ct = default);

    /// <summary>
    /// Apply the membership effect of a now-PAID membership FeeInvoice (Flow B confirmation):
    /// Renewal/Membership → extend PaidThroughUtc +1 year (from max(now, PaidThroughUtc)), Status=Active,
    /// snapshot PaidAmountTHB; Upgrade → switch tier to the invoice's target. Idempotent — safe to call once
    /// the slip is approved even if the membership was already advanced. Does NOT change the invoice itself.
    /// </summary>
    Task<Result<MembershipDto>> ConfirmInvoicePaidAsync(Guid feeInvoiceId, CancellationToken ct = default);

    /// <summary>FR-06/FR-10 gate: is the user's membership currently active (Trial or paid Active)?</summary>
    Task<bool> IsMembershipActiveAsync(Guid userId, CancellationToken ct = default);
}
