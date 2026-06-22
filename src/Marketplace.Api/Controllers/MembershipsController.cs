using Marketplace.Application.Memberships;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

/// <summary>
/// Membership lifecycle (FR-05, FR-27). Annual fees billed via FeeInvoices — separate from
/// buyer/seller money (no-touch). Renew/Upgrade issue an Issued invoice only; the membership is
/// activated/extended when the fee is confirmed paid (Flow B: slip upload + Admin approval).
/// </summary>
[ApiController]
[Route("api/memberships")]
[Authorize]
public class MembershipsController : ApiControllerBase
{
    private readonly IMembershipService _membership;
    private readonly MarketplaceDbContext _db;

    public MembershipsController(IMembershipService membership, MarketplaceDbContext db)
    {
        _membership = membership;
        _db = db;
    }

    /// <summary>FR-27: current membership (status + trial/paid expiry + auto-renew) for the caller.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);

        var dto = await _db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new MembershipDto(
                m.MembershipId, m.UserId, m.MembershipTierId, m.Status,
                m.TrialEndsAtUtc, m.PaidThroughUtc, m.AutoRenew))
            .FirstOrDefaultAsync(ct);

        return dto is null ? Problem("No membership found.") : Ok(dto);
    }

    /// <summary>FR-27: request renewal — issues a FeeInvoice (Issued) only; sets the auto-renew preference.</summary>
    [HttpPost("renew")]
    public async Task<IActionResult> Renew([FromBody] RenewBody body, CancellationToken ct)
    {
        var membershipId = await ResolveOwnMembershipIdAsync(ct);
        if (membershipId is null)
            return Problem("No membership found.");

        var result = await _membership.RenewAsync(new RenewMembershipRequest(membershipId.Value, body.AutoRenew), ct);
        return FromResult(result);
    }

    /// <summary>FR-05: request a tier upgrade — issues a pro-rated FeeInvoice (Issued) only.</summary>
    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade([FromBody] UpgradeBody body, CancellationToken ct)
    {
        var membershipId = await ResolveOwnMembershipIdAsync(ct);
        if (membershipId is null)
            return Problem("No membership found.");

        var result = await _membership.UpgradeTierAsync(new UpgradeTierRequest(membershipId.Value, body.NewTierId), ct);
        return FromResult(result);
    }

    /// <summary>FR-27: toggle auto-renew (can be turned off any time before the cut-off).</summary>
    [HttpPost("auto-renew")]
    public async Task<IActionResult> SetAutoRenew([FromBody] AutoRenewBody body, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);

        var membership = await _db.Memberships
            .Where(m => m.UserId == userId &&
                        (m.Status == MembershipStatus.Trial || m.Status == MembershipStatus.Active))
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (membership is null)
            return Problem("No live membership found.");

        membership.AutoRenew = body.AutoRenew;
        membership.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The caller's single live (Trial/Active) membership id, or null if none.</summary>
    private async Task<Guid?> ResolveOwnMembershipIdAsync(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return null;
        return await _db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId &&
                        (m.Status == MembershipStatus.Trial || m.Status == MembershipStatus.Active))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => (Guid?)m.MembershipId)
            .FirstOrDefaultAsync(ct);
    }

    public sealed record RenewBody(bool AutoRenew);
    public sealed record UpgradeBody(byte NewTierId);
    public sealed record AutoRenewBody(bool AutoRenew);
}
