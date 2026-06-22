using System.Security.Claims;
using Marketplace.Application.Credits;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Credits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-28/FR-29: credit wallet — balance + transparent append-only ledger + single-level referral CTA.
///
/// LEGAL INVARIANT (must not regress): credit is non-cashable, non-transferable. This controller has
/// NO withdraw / cash-out / transfer action by design (outside พ.ร.บ.ระบบการชำระเงิน 2560).
/// </summary>
[Authorize]
public class CreditsController : Controller
{
    private readonly ICreditService _creditService;
    private readonly MarketplaceDbContext _db;

    public CreditsController(ICreditService creditService, MarketplaceDbContext db)
    {
        _creditService = creditService;
        _db = db;
    }

    /// <summary>GET /Credits — balance hero + ledger (FR-29) + non-cashable warning + referral CTA.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
            return Challenge();

        // Balance + ledger via the service (returns zero/empty gracefully when no account/rows exist yet).
        var balanceResult = await _creditService.GetBalanceAsync(userId.Value, ct);
        var ledgerResult = await _creditService.GetLedgerAsync(userId.Value, ct);

        var rows = (ledgerResult.Succeeded && ledgerResult.Value is not null
                ? ledgerResult.Value
                : System.Array.Empty<CreditLedgerEntryDto>())
            .Select(e => new CreditLedgerRow(
                e.CreatedAtUtc,
                DescribeType(e.Type, e.Amount),
                e.Type,
                e.Amount,
                e.BalanceAfter,
                e.ExpiresAtUtc))
            .ToList();

        // FR-28 single-level: the user's own active referral code for the invite CTA (null hides the button).
        var referralCode = await _db.ReferralCodes.AsNoTracking()
            .Where(c => c.UserId == userId.Value && c.IsActive)
            .Select(c => c.Code)
            .FirstOrDefaultAsync(ct);

        var vm = new CreditsViewModel
        {
            Balance = balanceResult.Succeeded && balanceResult.Value is not null ? balanceResult.Value.Balance : 0m,
            ReferralCode = referralCode,
            Ledger = rows,
        };
        return View(vm);
    }

    /// <summary>Human-readable ledger description per transaction type (no money/cash-out wording, FR-28/29).</summary>
    private static string DescribeType(CreditTransactionType type, decimal amount) => type switch
    {
        CreditTransactionType.ReferralReward => "เครดิตจากการแนะนำเพื่อน (single-level)",
        CreditTransactionType.PromoSpend => "ใช้เครดิตโปรโมตประกาศ",
        CreditTransactionType.Expiry => "เครดิตหมดอายุ",
        CreditTransactionType.Revoke => "เรียกคืนเครดิต (ตรวจพบการใช้ผิดเงื่อนไข)",
        CreditTransactionType.Adjustment => amount >= 0 ? "ปรับปรุงเครดิต (เพิ่ม)" : "ปรับปรุงเครดิต (ลด)",
        _ => "รายการเครดิต",
    };

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
