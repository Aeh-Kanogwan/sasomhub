using System.Security.Claims;
using Marketplace.Application.Configuration;
using Marketplace.Web.Models.AdminConfig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Web.Controllers;

/// <summary>
/// M7 — FR-31 (B-04/G-4) Admin config versioning (RBAC: [Authorize(Roles="Admin")]). Lists each
/// editable config key with its CURRENT effective value, shows the append-only version history for a
/// key, and appends a NEW version (price/credit/expiry/bank account) that takes effect from a chosen
/// instant onward.
///
/// IMMUTABILITY: editing here NEVER updates an existing row — every save INSERTs a new ConfigVersion
/// (the history is append-only, enforced in the DB). A new version is NOT retroactive: cycles already
/// charged keep their snapshotted price.
///
/// SEPARATE FILE by design (route prefix /AdminConfig): keeps M7's admin surface out of
/// AdminController.cs so parallel module agents do not textually conflict in that shared file.
/// </summary>
[Authorize(Roles = "Admin")]
public class AdminConfigController : Controller
{
    private readonly IConfigAdminService _config;

    public AdminConfigController(IConfigAdminService config) => _config = config;

    /// <summary>GET /AdminConfig — every config key with the value currently in effect (now).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await _config.GetCurrentAsync(ct);
        return View(new AdminConfigIndexViewModel
        {
            Current = result.Succeeded ? result.Value! : new List<ConfigVersionDto>(),
        });
    }

    /// <summary>GET /AdminConfig/History?key=... — append-only version timeline + the new-version form.</summary>
    [HttpGet]
    public async Task<IActionResult> History(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) return RedirectToAction(nameof(Index));

        var result = await _config.GetHistoryAsync(key, ct);
        return View(new AdminConfigHistoryViewModel
        {
            ConfigKey = key,
            History = result.Succeeded ? result.Value! : new List<ConfigVersionDto>(),
            NewEffectiveFromUtc = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// POST /AdminConfig/Append — append a NEW immutable version for the key. Rejects an
    /// EffectiveFromUtc that collides with an existing version, and validates the value per key.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Append(string key, string value, DateTime effectiveFromUtc, string? note, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        // The web form supplies an Unspecified-kind datetime (datetime-local input); treat it as UTC.
        var effective = DateTime.SpecifyKind(effectiveFromUtc, DateTimeKind.Utc);

        var result = await _config.SetAsync(
            new SetConfigRequest(key, value, effective, adminId.Value, note), ct);

        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? $"เพิ่มเวอร์ชันใหม่ของ '{key}' เรียบร้อยแล้ว (ไม่กระทบรอบที่ชำระไปแล้ว)"
            : (result.Error ?? "เพิ่มเวอร์ชันไม่สำเร็จ");

        return RedirectToAction(nameof(History), new { key });
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
