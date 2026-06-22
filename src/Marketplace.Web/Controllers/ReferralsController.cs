using System.Security.Claims;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-28 (SINGLE-LEVEL): the user's referral hub — their own code + the people they personally invited
/// + non-cashable credit earned.
///
/// LEGAL INVARIANT (must not regress): single-level only. There is NO upline/downline/level/tree view here;
/// we only list direct invitees (Referrals where ReferrerUserId == caller). Rewards are non-cashable platform
/// credit, never cash (พ.ร.บ.ขายตรงและตลาดแบบตรง / พ.ร.บ.ระบบการชำระเงิน 2560).
/// </summary>
[Authorize]
public class ReferralsController : Controller
{
    private readonly MarketplaceDbContext _db;

    public ReferralsController(MarketplaceDbContext db) => _db = db;

    /// <summary>GET /Referrals — own code (share CTA) + invited list + credit earned. Degrades to empty gracefully.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
            return Challenge();

        // The caller's own active code (single-level: one per user). Null hides/disables the share CTA.
        var myCode = await _db.ReferralCodes.AsNoTracking()
            .Where(c => c.UserId == userId.Value && c.IsActive)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(ct);

        // Direct invitees only (single-level). Show invitee display name (PDPA-minimal — no email/phone).
        var invited = await _db.Referrals.AsNoTracking()
            .Where(r => r.ReferrerUserId == userId.Value)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new ReferralRow(
                r.Referred.Profile != null ? r.Referred.Profile.DisplayName : "นักสะสม",
                r.Status,
                r.RewardCreditToReferrer,
                r.CreatedAtUtc,
                r.RewardedAtUtc))
            .ToListAsync(ct);

        var vm = new ReferralsViewModel
        {
            MyReferralCode = myCode,
            Invited = invited,
            TotalCreditEarned = invited
                .Where(r => r.Status == ReferralStatus.Rewarded)
                .Sum(r => r.CreditToReferrer),
            RewardedCount = invited.Count(r => r.Status == ReferralStatus.Rewarded),
        };
        return View(vm);
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
