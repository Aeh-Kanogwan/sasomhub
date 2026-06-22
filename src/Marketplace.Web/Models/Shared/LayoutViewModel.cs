using Marketplace.Domain.Enums;

namespace Marketplace.Web.Models.Shared;

/// <summary>
/// Per-request header/footer state surfaced in _Layout (navbar login/membership status).
/// Populated from the cookie principal claims (TODO: backend-dev — set these claims at sign-in).
/// Guests see login/register CTAs; members see their tier + membership status badge (FR-08/FR-27).
/// </summary>
public class LayoutViewModel
{
    public bool IsAuthenticated { get; init; }
    public string? DisplayName { get; init; }

    /// <summary>Tier label shown next to the user (Normal/Verified/Premium). Null for guests.</summary>
    public string? MembershipTierLabel { get; init; }

    /// <summary>Trial/Active/Expired/Cancelled — drives the badge style in the navbar (FR-27).</summary>
    public MembershipStatus? MembershipStatus { get; init; }

    /// <summary>FR-06/FR-10 gate convenience flag (Trial or paid Active). Mirrors the "MembershipActive" claim.</summary>
    public bool IsMembershipActive { get; init; }

    /// <summary>Live credit balance for a quick navbar/credits glance (FR-29). Null if not loaded.</summary>
    public decimal? CreditBalance { get; init; }
}
