using System.Security.Claims;
using Marketplace.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Marketplace.Web.Controllers;

/// <summary>
/// M6 — FR-07 / LEGAL #2: create / publish independent appraiser OPINIONS on a product. The platform
/// does NOT warrant authenticity; each row stores the disclaimer version shown at create time and the
/// service rejects warranty wording ("รับประกัน" / "การันตี" / "ของแท้ 100%" / "guarantee" /
/// "100% authentic").
///
/// RBAC: [Authorize(Roles="Admin")] for now. UserRole has no dedicated "Appraiser" role yet
/// (enum = Member, Admin, Support), so per the M6 brief the write surface is gated to Admin until that
/// role exists — widen the policy here once it is added.
///
/// SEPARATE FILE by design: keeps M6's surface out of ListingsController / AdminController so parallel
/// module agents do not textually conflict. The PUBLIC product-detail page already renders published
/// opinions (ListingsController, read-only) — this controller is the write/review surface. Route: /Appraisal.
/// </summary>
[Authorize(Roles = "Admin")]
public class AppraisalController : Controller
{
    private readonly IAppraisalService _appraisals;

    public AppraisalController(IAppraisalService appraisals) => _appraisals = appraisals;

    /// <summary>GET /Appraisal/Product/{productId} — all opinions on a product (incl. drafts) for review.</summary>
    [HttpGet]
    public async Task<IActionResult> Product(Guid productId, CancellationToken ct)
    {
        var result = await _appraisals.GetForProductAsync(productId, includeUnpublished: true, ct);
        if (!result.Succeeded)
        {
            TempData["AdminError"] = result.Error ?? "ไม่สามารถโหลดความเห็นได้";
            return RedirectToAction("Index", "Home");
        }

        ViewData["ProductId"] = productId;
        return View(result.Value);
    }

    /// <summary>POST /Appraisal/Create — create an opinion (draft, or publish immediately).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid productId, string appraiserName, string opinionText, bool publish, CancellationToken ct)
    {
        // Bind the acting admin as the appraiser user (FK) and audit actor.
        var actorId = GetUserId();
        var request = new CreateAppraisalRequest(productId, actorId, appraiserName, opinionText, publish);

        var result = await _appraisals.CreateAsync(request, ct);
        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? (publish ? "บันทึกและเผยแพร่ความเห็นเรียบร้อยแล้ว (เป็นความเห็น ไม่ใช่การรับประกัน)"
                       : "บันทึกความเห็นเป็นฉบับร่างเรียบร้อยแล้ว")
            : (result.Error ?? "บันทึกความเห็นไม่สำเร็จ");

        return RedirectToAction(nameof(Product), new { productId });
    }

    /// <summary>POST /Appraisal/Publish — publish a previously-drafted opinion.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid appraisalOpinionId, Guid productId, CancellationToken ct)
    {
        var actorId = GetUserId();
        if (actorId is null) return Forbid();

        var result = await _appraisals.PublishAsync(appraisalOpinionId, actorId.Value, ct);
        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? "เผยแพร่ความเห็นเรียบร้อยแล้ว"
            : (result.Error ?? "เผยแพร่ไม่สำเร็จ");

        return RedirectToAction(nameof(Product), new { productId });
    }

    /// <summary>POST /Appraisal/Unpublish — pull a published opinion back into review.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(Guid appraisalOpinionId, Guid productId, CancellationToken ct)
    {
        var actorId = GetUserId();
        if (actorId is null) return Forbid();

        var result = await _appraisals.UnpublishAsync(appraisalOpinionId, actorId.Value, ct);
        TempData[result.Succeeded ? "AdminOk" : "AdminError"] = result.Succeeded
            ? "นำความเห็นออกจากการเผยแพร่แล้ว"
            : (result.Error ?? "ดำเนินการไม่สำเร็จ");

        return RedirectToAction(nameof(Product), new { productId });
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
