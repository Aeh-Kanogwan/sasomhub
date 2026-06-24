using System.Security.Claims;
using Marketplace.Application.Disputes;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Disputes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-21 — a party to a no-touch transaction opens / views their disputes. Mediation only:
/// nothing here moves money (the platform never pays or refunds — LEGAL #1). Authorization is
/// double-checked: the raiser must be the buyer or seller of the transaction, and detail/list
/// only ever show disputes the current user is involved in.
/// </summary>
[Authorize]
public class DisputesController : Controller
{
    private const string ResultKey = "DisputeResult";   // ok | error
    private const string MessageKey = "DisputeMessage";  // Thai message

    private readonly IDisputeService _disputes;
    private readonly MarketplaceDbContext _db;
    private readonly ILogger<DisputesController> _logger;

    public DisputesController(IDisputeService disputes, MarketplaceDbContext db, ILogger<DisputesController> logger)
    {
        _disputes = disputes;
        _db = db;
        _logger = logger;
    }

    /// <summary>GET /Disputes — the current user's disputes (raised by them or on their transactions).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();

        var result = await _disputes.GetForUserAsync(userId.Value, ct);
        return View(new MyDisputesViewModel
        {
            Disputes = result.Succeeded ? result.Value! : new List<DisputeDto>()
        });
    }

    /// <summary>GET /Disputes/Raise/{transactionId} — open-dispute form. Party-only.</summary>
    [HttpGet]
    public async Task<IActionResult> Raise(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();

        // FR-21: only a party (buyer or seller) of the transaction may open this form.
        var isParty = await _db.Transactions.AsNoTracking()
            .AnyAsync(t => t.TransactionId == id && (t.BuyerId == userId.Value || t.SellerId == userId.Value), ct);
        if (!isParty) return Forbid();

        return View(new RaiseDisputeViewModel
        {
            TransactionId = id,
            ReasonCodes = await LoadReasonCodesAsync(ct),
        });
    }

    /// <summary>POST /Disputes/Raise — record a new dispute (status Open). No money moves.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Raise(RaiseDisputeViewModel form, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();

        var result = await _disputes.RaiseAsync(
            new RaiseDisputeRequest(form.TransactionId, userId.Value, form.ReasonCodeId, form.Description), ct);

        if (result.Succeeded)
        {
            TempData[ResultKey] = "ok";
            TempData[MessageKey] = "เปิดข้อพิพาทเรียบร้อยแล้ว ทีมงานจะตรวจสอบและไกล่เกลี่ย (แพลตฟอร์มไม่จ่าย/ไม่คืนเงิน)";
            return RedirectToAction(nameof(Detail), new { id = result.Value!.DisputeId });
        }

        TempData[ResultKey] = "error";
        TempData[MessageKey] = result.Error ?? "ไม่สามารถเปิดข้อพิพาทได้";
        var redo = new RaiseDisputeViewModel
        {
            TransactionId = form.TransactionId,
            ReasonCodeId = form.ReasonCodeId,
            Description = form.Description,
            ReasonCodes = await LoadReasonCodesAsync(ct),
        };
        return View(redo);
    }

    /// <summary>GET /Disputes/Detail/{id} — view a single dispute. Party-only.</summary>
    [HttpGet]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();

        var result = await _disputes.GetAsync(id, ct);
        if (!result.Succeeded) return NotFound();

        var dto = result.Value!;
        // Only the raiser or a party to the underlying transaction may view it.
        var isInvolved = dto.RaisedByUserId == userId.Value
            || await _db.Transactions.AsNoTracking()
                .AnyAsync(t => t.TransactionId == dto.TransactionId
                               && (t.BuyerId == userId.Value || t.SellerId == userId.Value), ct);
        if (!isInvolved) return Forbid();

        return View(new DisputeDetailViewModel { Dispute = dto });
    }

    private async Task<IReadOnlyList<ReasonCodeOption>> LoadReasonCodesAsync(CancellationToken ct)
        => await _db.BlacklistReasonCodes.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.ReasonCodeId)
            .Select(r => new ReasonCodeOption(r.ReasonCodeId, r.DisplayName))
            .ToListAsync(ct);

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
