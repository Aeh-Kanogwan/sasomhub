using System.Security.Claims;
using Marketplace.Application.Payments;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Web.Controllers;

/// <summary>
/// Admin dashboard (RBAC: [Authorize(Roles="Admin")]). Flow B: review the member-uploaded membership-payment
/// slips and Approve/Reject them. Approval marks the company FeeInvoice Paid and applies the membership effect
/// (renew/upgrade). Membership fees are company money — separate from buyer↔seller trade money (no-touch).
/// </summary>
[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly IPaymentSlipService _paymentSlipService;
    private readonly MarketplaceDbContext _db;

    public AdminController(IPaymentSlipService paymentSlipService, MarketplaceDbContext db)
    {
        _paymentSlipService = paymentSlipService;
        _db = db;
    }

    /// <summary>GET /Admin — dashboard counters (pending slips, open disputes, pending ban reviews).</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var pendingSlips = await _db.PaymentSlips.AsNoTracking()
            .CountAsync(s => s.Status == PaymentSlipStatus.Pending, ct);
        var openDisputes = await _db.Disputes.AsNoTracking()
            .CountAsync(d => d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview, ct);
        var pendingBans = await _db.Users.AsNoTracking()
            .CountAsync(u => u.AccountStatus == AccountStatus.PendingBanReview, ct);

        return View(new AdminDashboardViewModel
        {
            PendingSlipCount = pendingSlips,
            OpenDisputeCount = openDisputes,
            PendingBanReviewCount = pendingBans,
        });
    }

    /// <summary>GET /Admin/Payments — the Pending payment-slip queue.</summary>
    [HttpGet]
    public async Task<IActionResult> Payments(CancellationToken ct)
    {
        var pending = await _paymentSlipService.ListPendingSlipsAsync(ct);
        return View(new AdminPaymentsViewModel { Pending = pending });
    }

    /// <summary>GET /Admin/PaymentDetail/{slipId} — full slip + invoice/user detail with Approve/Reject.</summary>
    [HttpGet]
    public async Task<IActionResult> PaymentDetail(Guid slipId, CancellationToken ct)
    {
        var slip = await _paymentSlipService.GetSlipAsync(slipId, ct);
        if (slip is null) return NotFound();
        return View(new AdminPaymentDetailViewModel { Slip = slip });
    }

    /// <summary>POST /Admin/Approve — approve a pending slip (→ invoice Paid + membership effect).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid slipId, string? note, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _paymentSlipService.ApproveSlipAsync(slipId, adminId.Value, note, ct);
        TempData[result.Succeeded ? "AdminOk" : "AdminError"] =
            result.Succeeded ? "อนุมัติสลิปและยืนยันการชำระเรียบร้อยแล้ว" : (result.Error ?? "อนุมัติไม่สำเร็จ");
        return RedirectToAction(nameof(Payments));
    }

    /// <summary>POST /Admin/Reject — reject a pending slip (invoice stays Issued).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid slipId, string? note, CancellationToken ct)
    {
        var adminId = GetUserId();
        if (adminId is null) return Forbid();

        var result = await _paymentSlipService.RejectSlipAsync(slipId, adminId.Value, note, ct);
        TempData[result.Succeeded ? "AdminOk" : "AdminError"] =
            result.Succeeded ? "ปฏิเสธสลิปเรียบร้อยแล้ว" : (result.Error ?? "ปฏิเสธไม่สำเร็จ");
        return RedirectToAction(nameof(Payments));
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
