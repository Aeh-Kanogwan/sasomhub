using System.Security.Claims;
using Marketplace.Application.Privacy;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Web.Controllers;

/// <summary>
/// M3 PDPA DSAR — data-subject self-service (FR-01/04). A signed-in user lodges a request about THEIR
/// OWN data (export / erasure / access / rectify / withdraw-consent), checks its status, and downloads
/// their finished export. Admin handling lives in <see cref="AdminPrivacyController"/>.
/// </summary>
[Authorize]
public class PrivacyController : Controller
{
    /// <summary>Export download link TTL (days) — mirrors DataSubjectRequestService.ExportTtlDays.</summary>
    private const int ExportTtlDays = 7;

    private readonly IDataSubjectRequestService _dsar;
    private readonly MarketplaceDbContext _db;

    public PrivacyController(IDataSubjectRequestService dsar, MarketplaceDbContext db)
    {
        _dsar = dsar;
        _db = db;
    }

    /// <summary>GET /Privacy — list the signed-in user's own DSAR requests.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Challenge();

        var result = await _dsar.GetMyRequestsAsync(userId.Value, ct);
        return View(result.Value ?? new List<DataSubjectRequestDto>());
    }

    /// <summary>POST /Privacy/Create — lodge a new request (DueByUtc = now + 30 days, set in the service).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DsarType requestType, string? note, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Challenge();

        var result = await _dsar.CreateAsync(new CreateDsarRequest(userId.Value, requestType, note), ct);
        TempData[result.Succeeded ? "PrivacyOk" : "PrivacyError"] =
            result.Succeeded ? "ส่งคำขอเรียบร้อยแล้ว ทีมงานจะดำเนินการภายใน 30 วัน" : (result.Error ?? "ส่งคำขอไม่สำเร็จ");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// GET /Privacy/Download/{requestId} — stream the user's OWN completed export JSON. Enforces
    /// ownership and the 7-day TTL (the link expires after CompletedAtUtc + ExportTtlDays).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Download(Guid requestId, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Challenge();

        var req = await _db.DataSubjectRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.DataSubjectRequestId == requestId, ct);
        if (req is null) return NotFound();
        if (req.RequestedByUserId != userId.Value) return Forbid();           // own data only
        if (string.IsNullOrEmpty(req.ResultArtifactPath) || req.CompletedAtUtc is null)
            return NotFound();

        // TTL: the download link expires CompletedAtUtc + 7 days.
        if (DateTime.UtcNow > req.CompletedAtUtc.Value.AddDays(ExportTtlDays))
        {
            TempData["PrivacyError"] = "ลิงก์ดาวน์โหลดหมดอายุแล้ว กรุณายื่นคำขอใหม่";
            return RedirectToAction(nameof(Index));
        }

        if (!System.IO.File.Exists(req.ResultArtifactPath))
            return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(req.ResultArtifactPath, ct);
        return File(bytes, "application/json", $"my-data-{requestId:N}.json");
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
