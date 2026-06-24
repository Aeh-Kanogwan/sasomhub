using System.Security.Claims;
using Marketplace.Application.Kyc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Web.Controllers;

/// <summary>
/// M2 (FR-02/FR-06): e-KYC via NDID for the Verified/Premium tiers. RBAC: every action requires login —
/// a user may only start / read THEIR OWN verification (UserId is taken from the auth ticket, never the
/// request body). The provider callback endpoint is anonymous + reconciled by provider reference.
/// </summary>
[Authorize]
public class KycController : Controller
{
    private readonly IKycService _kycService;

    public KycController(IKycService kycService) => _kycService = kycService;

    /// <summary>GET /Kyc — current KYC status for the signed-in user (drives tier eligibility).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Challenge();

        var result = await _kycService.GetStatusAsync(userId.Value, ct);
        if (!result.Succeeded)
        {
            TempData["KycError"] = result.Error ?? "ไม่สามารถอ่านสถานะ KYC ได้";
            return View((KycStatusDto?)null);
        }
        return View(result.Value);
    }

    /// <summary>
    /// POST /Kyc/Start — FR-02: begin e-KYC for the signed-in user at the requested level (Verified/Premium).
    /// Returns the provider handoff (redirect/QR) so the UI can send the user to NDID.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(KycLevel targetLevel, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Challenge();

        var result = await _kycService.StartVerificationAsync(new StartKycRequest(userId.Value, targetLevel), ct);
        if (!result.Succeeded)
        {
            TempData["KycError"] = result.Error ?? "เริ่มทำ KYC ไม่สำเร็จ";
            return RedirectToAction(nameof(Index));
        }

        // In Mock mode there is no redirect; the caller polls status. In Ndid mode RedirectUrl is the handoff.
        if (!string.IsNullOrWhiteSpace(result.Value!.RedirectUrl))
            return Redirect(result.Value.RedirectUrl!);

        TempData["KycOk"] = "เริ่มกระบวนการยืนยันตัวตนแล้ว";
        return RedirectToAction(nameof(Index));
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
