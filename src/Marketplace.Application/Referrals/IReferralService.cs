using Marketplace.Application.Common;

namespace Marketplace.Application.Referrals;

// FR-28: SINGLE-LEVEL referral only. A referrer earns ONLY from users they personally invited.
// No upline/downline/multi-tier. Rewards are non-cashable platform credit, never cash.

public record RegisterReferralRequest(Guid ReferredUserId, string ReferralCode);

public record ReferralDto(Guid ReferralId, Guid ReferrerUserId, Guid ReferredUserId, string Status);

/// <summary>Single-level referral service (FR-28). Skeleton.</summary>
public interface IReferralService
{
    /// <summary>FR-28: link a new user to a referrer via code (a user can be referred only once, no self-referral).</summary>
    Task<Result<ReferralDto>> RegisterAsync(RegisterReferralRequest request, CancellationToken ct = default);

    /// <summary>FR-28: pay out single-level reward as credit once the trigger condition is met (e.g. first paid membership).</summary>
    Task<Result> QualifyAndRewardAsync(Guid referralId, CancellationToken ct = default);

    /// <summary>FR-28: revoke credit gained by abuse (self-referral / duplicate device/IP/KYC).</summary>
    Task<Result> RevokeForAbuseAsync(Guid referralId, string reason, CancellationToken ct = default);
}
