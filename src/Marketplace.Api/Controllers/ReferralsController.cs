using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

/// <summary>
/// Referral endpoints (FR-28). SINGLE-LEVEL only — rewards paid as non-cashable credit, never cash.
/// No upline/downline structure exists by design.
/// </summary>
[ApiController]
[Route("api/referrals")]
[Authorize]
public class ReferralsController : ApiControllerBase
{
    private readonly MarketplaceDbContext _db;

    public ReferralsController(MarketplaceDbContext db) => _db = db;

    /// <summary>FR-28: the caller's single referral code (created at registration; one per user).</summary>
    [HttpGet("my-code")]
    public async Task<IActionResult> MyCode(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);

        var code = await _db.ReferralCodes.AsNoTracking()
            .Where(c => c.UserId == userId && c.IsActive)
            .Select(c => new MyCodeDto(c.Code, c.IsActive, c.CreatedAtUtc))
            .FirstOrDefaultAsync(ct);

        return code is null ? Problem("Referral code not found.") : Ok(code);
    }

    /// <summary>FR-28: users this caller personally referred (single-level only — direct invitees only).</summary>
    [HttpGet("my-referrals")]
    public async Task<IActionResult> MyReferrals(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);

        var rows = await _db.Referrals.AsNoTracking()
            .Where(r => r.ReferrerUserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new MyReferralDto(
                r.ReferralId, r.ReferredUserId, r.Status.ToString(),
                r.RewardCreditToReferrer, r.RewardedAtUtc, r.CreatedAtUtc))
            .ToListAsync(ct);

        return Ok(rows);
    }

    // NOTE: no upline/downline/multi-level endpoint — flat by design (avoids MLM/pyramid).

    public sealed record MyCodeDto(string Code, bool IsActive, DateTime CreatedAtUtc);
    public sealed record MyReferralDto(
        Guid ReferralId, Guid ReferredUserId, string Status,
        decimal RewardCreditToReferrer, DateTime? RewardedAtUtc, DateTime CreatedAtUtc);
}
