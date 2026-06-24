using System.Security.Claims;
using Marketplace.Application.Disputes;
using Marketplace.Web.Models.Disputes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-21 — Admin dispute mediation queue (RBAC: [Authorize(Roles="Admin")]). Lists open disputes,
/// shows detail, and transitions Open -> UnderReview -> Resolved | Rejected | Escalated. Mediation
/// only: the platform NEVER pays or refunds (LEGAL #1) — admins record an outcome, not a money move.
///
/// SEPARATE FILE by design: keeps M4's admin surface out of AdminController.cs so parallel module
/// agents do not textually conflict in that shared file. Route prefix: /AdminDisputes.
/// </summary>
[Authorize(Roles = "Admin")]
public class AdminDisputesController : Controller
{
    private readonly IDisputeService _disputes;

    public AdminDisputesController(IDisputeService disputes) => _disputes = disputes;

    /// <summary>GET /AdminDisputes — the dispute queue, oldest-first. ?status= filters (default Open).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        // Default the queue to actionable (Open) disputes; empty string = all.
        var filter = status ?? nameof(Marketplace.Domain.Enums.DisputeStatus.Open);
        var effective = string.IsNullOrWhiteSpace(filter) ? null : filter;

        var result = await _disputes.GetForAdminAsync(effective, ct);
        return View(new AdminDisputesViewModel
        {
            StatusFilter = effective,
            Disputes = result.Succeeded ? result.Value! : new List<DisputeDto>(),
        });
    }

    /// <summary>GET /AdminDisputes/Detail/{id} — full dispute with transition actions.</summary>
    [HttpGet]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var result = await _disputes.GetAsync(id, ct);
        if (!result.Succeeded) return NotFound();
        return View(new AdminDisputeDetailViewModel { Dispute = result.Value! });
    }

    /// <summary>POST /AdminDisputes/Transition — move the dispute to a new status (with resolution note).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transition(Guid id, string newStatus, string? resolution, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _disputes.TransitionAsync(
            new ResolveDisputeRequest(id, adminId.Value, newStatus, resolution), ct);

        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? $"อัปเดตสถานะข้อพิพาทเป็น {newStatus} เรียบร้อยแล้ว (ไม่มีการเคลื่อนย้ายเงิน)"
            : (result.Error ?? "อัปเดตสถานะไม่สำเร็จ");

        return RedirectToAction(nameof(Detail), new { id });
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
