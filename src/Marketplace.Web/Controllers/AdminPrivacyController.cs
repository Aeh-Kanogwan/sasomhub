using System.Security.Claims;
using Marketplace.Application.Privacy;
using Marketplace.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Web.Controllers;

/// <summary>
/// M3 PDPA DSAR — Admin handling (FR-01/04). Separate controller (not folded into AdminController)
/// to avoid merge conflicts with the payment-queue work in AdminController. RBAC: Admin only.
/// Lists requests, claims/advances them, fulfils Export/Erasure, or rejects with a reason. Every
/// action is audited inside the service (FR-24).
/// </summary>
[Authorize(Roles = "Admin")]
[Route("Admin/Privacy")]
public class AdminPrivacyController : Controller
{
    private readonly IDataSubjectRequestService _dsar;

    public AdminPrivacyController(IDataSubjectRequestService dsar) => _dsar = dsar;

    /// <summary>GET /Admin/Privacy — the DSAR queue (optionally filtered by status), oldest-due first.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        var result = await _dsar.GetForAdminAsync(status, ct);
        if (!result.Succeeded)
        {
            TempData["AdminError"] = result.Error;
            return View(new List<DataSubjectRequestDto>());
        }
        ViewBag.StatusFilter = status;
        return View(result.Value ?? new List<DataSubjectRequestDto>());
    }

    /// <summary>POST /Admin/Privacy/Assign — claim a request and move it to InProgress.</summary>
    [HttpPost("Assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid requestId, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _dsar.AssignAsync(requestId, adminId.Value, ct);
        SetFlash(result.Succeeded, "รับเรื่องแล้ว (กำลังดำเนินการ)", result.Error);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Admin/Privacy/Export — generate the export package + mark Completed.</summary>
    [HttpPost("Export")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(Guid requestId, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _dsar.FulfilExportAsync(requestId, adminId.Value, ct);
        SetFlash(result.Succeeded, "สร้างไฟล์ข้อมูลส่วนบุคคลเรียบร้อยแล้ว", result.Error);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Admin/Privacy/Erase — anonymize the subject (keep legal-retention rows) + mark Completed.</summary>
    [HttpPost("Erase")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Erase(Guid requestId, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _dsar.FulfilErasureAsync(requestId, adminId.Value, ct);
        SetFlash(result.Succeeded, "ลบ/ปกปิดข้อมูลส่วนบุคคลเรียบร้อยแล้ว (คงข้อมูลที่ต้องเก็บตามกฎหมาย)", result.Error);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Admin/Privacy/Reject — reject a request with a reason.</summary>
    [HttpPost("Reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid requestId, string reason, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _dsar.RejectAsync(requestId, adminId.Value, reason, ct);
        SetFlash(result.Succeeded, "ปฏิเสธคำขอเรียบร้อยแล้ว", result.Error);
        return RedirectToAction(nameof(Index));
    }

    private void SetFlash(bool ok, string okMessage, string? error)
        => TempData[ok ? "AdminOk" : "AdminError"] = ok ? okMessage : (error ?? "ดำเนินการไม่สำเร็จ");

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
