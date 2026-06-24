using System.Security.Claims;
using Marketplace.Application.Reputation;
using Marketplace.Domain.Enums;
using Marketplace.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Web.Controllers;

/// <summary>
/// M5 — FR-17 Admin blacklist due-process surface (RBAC: [Authorize(Roles="Admin")]). Lists pending
/// ban-review proposals + blacklist entries, and lets an Admin CONFIRM a ban, REJECT a proposal, or
/// LIFT (unban) an active entry — each with an internal note. Human-in-the-loop only (DP-3): there is
/// never an automatic permanent ban; the Admin confirm here is the single place a ban is applied.
///
/// SEPARATE FILE by design: keeps M5's admin surface out of AdminController.cs so parallel module
/// agents do not textually conflict in that shared file. Route prefix: /AdminBlacklist.
/// </summary>
[Authorize(Roles = "Admin")]
public class AdminBlacklistController : Controller
{
    private readonly IBlacklistService _blacklist;

    public AdminBlacklistController(IBlacklistService blacklist) => _blacklist = blacklist;

    /// <summary>GET /AdminBlacklist — entries by review status. ?status= filters (default PendingReview queue).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        // Default the queue to actionable proposals (PendingReview); empty string = all.
        var filter = status ?? nameof(BlacklistReviewStatus.PendingReview);
        var effective = string.IsNullOrWhiteSpace(filter) ? null : filter;

        var result = await _blacklist.GetForAdminAsync(effective, ct);
        return View(new AdminBlacklistViewModel
        {
            StatusFilter = effective,
            Entries = result.Succeeded ? result.Value! : new List<BlacklistEntryDto>(),
        });
    }

    /// <summary>POST /AdminBlacklist/Confirm — Admin confirms a pending proposal (-> Banned).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid id, string? note, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _blacklist.ReviewAsync(
            new ReviewBlacklistRequest(id, adminId.Value, Confirm: true, note), ct);

        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? "ยืนยันการแบนแล้ว (ผ่านการพิจารณาโดยผู้ดูแล)"
            : (result.Error ?? "ยืนยันการแบนไม่สำเร็จ");

        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /AdminBlacklist/Reject — Admin rejects a pending proposal (account restored).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? note, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _blacklist.ReviewAsync(
            new ReviewBlacklistRequest(id, adminId.Value, Confirm: false, note), ct);

        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? "ปฏิเสธข้อเสนอแบนแล้ว และคืนสถานะบัญชี"
            : (result.Error ?? "ปฏิเสธข้อเสนอไม่สำเร็จ");

        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /AdminBlacklist/Unban — Admin lifts an active confirmed entry (account restored).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unban(Guid id, string? note, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _blacklist.UnbanAsync(id, adminId.Value, note, ct);

        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? "ปลดแบน / ยกเลิกรายการเตือนแล้ว"
            : (result.Error ?? "ปลดแบนไม่สำเร็จ");

        return RedirectToAction(nameof(Index));
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
