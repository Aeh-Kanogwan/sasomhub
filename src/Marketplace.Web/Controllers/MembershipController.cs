using System.Security.Claims;
using Marketplace.Application.Kyc;
using Marketplace.Application.Memberships;
using Marketplace.Application.Payments;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Marketplace.Web.Models.Membership;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-05/FR-22/FR-27: membership plans (annual fee 3 tiers), 3-month trial disclosure, renew/upgrade,
/// auto-renew toggle. All fees billed via FeeInvoices — separate from buyer/seller money (no-touch).
/// </summary>
public class MembershipController : Controller
{
    private static readonly IReadOnlyDictionary<string, string[]> TierFeatures = new Dictionary<string, string[]>
    {
        ["Normal"] = new[] { "ลงขายการ์ดได้", "เข้าร่วมประมูลสดได้", "ดูประวัติราคาแบบเรียลไทม์", "กระเป๋าเครดิตพื้นฐาน" },
        ["Verified"] = new[] { "ทุกสิทธิ์ของ Normal", "ยืนยันตัวตน e-KYC ผ่าน NDID", "ป้าย Verified ข้างชื่อโปรไฟล์", "ลงการ์ดมูลค่าสูงได้" },
        ["Premium"] = new[] { "ทุกสิทธิ์ของ Verified", "โปรโมต listing ขึ้นหน้าแรก", "ลดค่าธรรมเนียมการขาย", "การ์ดวิเคราะห์ราคาเชิงลึก" },
    };

    // Flow B: image upload constraints (PNG/JPG, ≤8MB) — mirrors ListingsController.
    private const long MaxSlipBytes = 8 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = { "image/png", "image/jpeg" };
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };

    private readonly IMembershipService _membershipService;
    private readonly IPaymentSlipService _paymentSlipService;
    private readonly IKycService _kycService;
    private readonly MarketplaceDbContext _db;
    private readonly ConfigVersionResolver _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<MembershipController> _logger;

    public MembershipController(
        IMembershipService membershipService,
        IPaymentSlipService paymentSlipService,
        IKycService kycService,
        MarketplaceDbContext db,
        ConfigVersionResolver config,
        IWebHostEnvironment env,
        ILogger<MembershipController> logger)
    {
        _membershipService = membershipService;
        _paymentSlipService = paymentSlipService;
        _kycService = kycService;
        _db = db;
        _config = config;
        _env = env;
        _logger = logger;
    }

    /// <summary>GET /Membership — plan grid + current status + auto-renew. Guest can view plans/pricing (FR-27).</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // FR-27/FR-35: plans from MembershipTiers; effective annual price from ConfigVersions (fallback to lookup).
        var tiers = await _db.MembershipTiers.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.MembershipTierId)
            .ToListAsync(ct);

        var plans = new List<MembershipPlanOption>(tiers.Count);
        foreach (var t in tiers)
        {
            var price = await _config.GetDecimalAsync(
                ConfigKeys.MembershipAnnualPrice(t.Code), now, fallback: t.AnnualPriceTHB, ct);
            plans.Add(new MembershipPlanOption
            {
                MembershipTierId = t.MembershipTierId,
                Code = t.Code,
                DisplayName = t.DisplayName,
                AnnualPriceTHB = price,
                RequiresKyc = t.RequiresKyc,
                IsFeatured = t.Code == "Premium",
                Features = TierFeatures.TryGetValue(t.Code, out var f) ? f : System.Array.Empty<string>(),
            });
        }

        var vm = new MembershipViewModel { Plans = plans };

        // Current membership for the signed-in user (guests see plans only).
        var userId = GetUserId();
        if (userId is not null)
        {
            var current = await _db.Memberships.AsNoTracking()
                .Where(m => m.UserId == userId.Value)
                .OrderByDescending(m => m.CreatedAtUtc)
                .Select(m => new
                {
                    m.MembershipId,
                    m.Status,
                    m.MembershipTierId,
                    m.AutoRenew,
                    m.TrialEndsAtUtc,
                    m.PaidThroughUtc,
                })
                .FirstOrDefaultAsync(ct);

            if (current is not null)
            {
                // Flow B: surface the user's latest membership invoice so Index can show "pay / รอตรวจ / Paid".
                var latest = await _db.FeeInvoices.AsNoTracking()
                    .Where(f => f.UserId == userId.Value
                                && (f.FeeType == FeeType.Membership
                                    || f.FeeType == FeeType.MembershipRenewal
                                    || f.FeeType == FeeType.MembershipUpgrade))
                    .OrderByDescending(f => f.IssuedAtUtc)
                    .Select(f => new { f.FeeInvoiceId, f.Amount, f.Status })
                    .FirstOrDefaultAsync(ct);

                LatestInvoiceInfo? latestInfo = null;
                if (latest is not null)
                {
                    var hasPending = await _db.PaymentSlips.AsNoTracking()
                        .AnyAsync(s => s.FeeInvoiceId == latest.FeeInvoiceId && s.Status == PaymentSlipStatus.Pending, ct);
                    latestInfo = new LatestInvoiceInfo
                    {
                        FeeInvoiceId = latest.FeeInvoiceId,
                        Amount = latest.Amount,
                        Status = latest.Status,
                        HasPendingSlip = hasPending,
                    };
                }

                vm = new MembershipViewModel
                {
                    Plans = plans,
                    MembershipId = current.MembershipId,
                    CurrentStatus = current.Status,
                    CurrentTierId = current.MembershipTierId,
                    AutoRenew = current.AutoRenew,
                    TrialEndsAtUtc = current.TrialEndsAtUtc,
                    PaidThroughUtc = current.PaidThroughUtc,
                    LatestInvoice = latestInfo,
                };
            }
        }

        return View(vm);
    }

    /// <summary>
    /// POST /Membership/Renew — FR-27: issue a renewal FeeInvoice (Issued) then send the user to the
    /// "pay by bank transfer + upload slip" page (Flow B). Membership is NOT extended until Admin confirms.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Renew(Guid membershipId, bool autoRenew, CancellationToken ct)
    {
        if (!await OwnsMembershipAsync(membershipId, ct))
            return Forbid();

        var result = await _membershipService.RenewAsync(new RenewMembershipRequest(membershipId, autoRenew), ct);
        if (!result.Succeeded)
        {
            TempData["MembershipError"] = result.Error ?? "ออกใบแจ้งหนี้ต่ออายุไม่สำเร็จ";
            return RedirectToAction(nameof(Index));
        }

        var invoiceId = await LatestIssuedInvoiceIdAsync(membershipId, FeeType.MembershipRenewal, ct);
        if (invoiceId is null)
        {
            TempData["MembershipError"] = "ไม่พบใบแจ้งหนี้ที่เพิ่งสร้าง";
            return RedirectToAction(nameof(Index));
        }
        TempData["MembershipOk"] = "ออกใบแจ้งหนี้ต่ออายุแล้ว กรุณาโอนชำระและอัปโหลดสลิป";
        return RedirectToAction(nameof(Pay), new { invoiceId = invoiceId.Value });
    }

    /// <summary>
    /// POST /Membership/Upgrade — FR-05: issue a pro-rated upgrade FeeInvoice (Issued) then send the user to
    /// the pay/upload-slip page (Flow B). The tier is NOT switched until Admin confirms.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Upgrade(Guid membershipId, byte newTierId, CancellationToken ct)
    {
        if (!await OwnsMembershipAsync(membershipId, ct))
            return Forbid();

        // FR-02/FR-06 (M2): a tier that RequiresKyc (Verified/Premium) may only be granted to a user with a
        // current VERIFIED e-KYC. Gate here at the controller layer (no change to the shared MembershipService).
        var targetTier = await _db.MembershipTiers.AsNoTracking()
            .FirstOrDefaultAsync(t => t.MembershipTierId == newTierId, ct);
        if (targetTier is { RequiresKyc: true } && !await IsKycVerifiedAsync(ct))
        {
            TempData["MembershipError"] = "ระดับนี้ต้องยืนยันตัวตน (e-KYC) ก่อน กรุณาทำ KYC ให้สำเร็จแล้วลองอีกครั้ง";
            return RedirectToAction(nameof(Index));
        }

        var result = await _membershipService.UpgradeTierAsync(new UpgradeTierRequest(membershipId, newTierId), ct);
        if (!result.Succeeded)
        {
            TempData["MembershipError"] = result.Error ?? "ออกใบแจ้งหนี้อัปเกรดไม่สำเร็จ";
            return RedirectToAction(nameof(Index));
        }

        var invoiceId = await LatestIssuedInvoiceIdAsync(membershipId, FeeType.MembershipUpgrade, ct);
        if (invoiceId is null)
        {
            TempData["MembershipError"] = "ไม่พบใบแจ้งหนี้ที่เพิ่งสร้าง";
            return RedirectToAction(nameof(Index));
        }
        TempData["MembershipOk"] = "ออกใบแจ้งหนี้อัปเกรดแล้ว กรุณาโอนชำระและอัปโหลดสลิป";
        return RedirectToAction(nameof(Pay), new { invoiceId = invoiceId.Value });
    }

    /// <summary>
    /// GET /Membership/Pay/{invoiceId} — Flow B: show the amount due, the company bank account
    /// (from ConfigVersions), the slip-upload form, and the current slip status (if any).
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Pay(Guid invoiceId, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Challenge();

        var invoice = await _db.FeeInvoices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FeeInvoiceId == invoiceId && f.UserId == userId.Value, ct);
        if (invoice is null)
            return NotFound();

        var vm = await BuildPayViewModelAsync(invoice.FeeInvoiceId, ct);
        if (vm is null) return NotFound();
        vm.AmountClaimed = invoice.Amount;
        return View(vm);
    }

    /// <summary>
    /// POST /Membership/Pay/{invoiceId} — Flow B: validate + save the slip image under
    /// wwwroot/uploads/slips/{invoiceId}/ then submit it via IPaymentSlipService for Admin review.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Pay(Guid invoiceId, PayInvoiceViewModel model, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Challenge();

        var invoice = await _db.FeeInvoices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FeeInvoiceId == invoiceId && f.UserId == userId.Value, ct);
        if (invoice is null)
            return NotFound();

        // Validate the uploaded slip image (PNG/JPG, ≤8MB) — same rules as listing photos (FR-06).
        var file = model.SlipImage;
        if (file is null || file.Length <= 0)
            ModelState.AddModelError(nameof(model.SlipImage), "กรุณาอัปโหลดรูปสลิปการโอน");
        else
        {
            if (file.Length > MaxSlipBytes)
                ModelState.AddModelError(nameof(model.SlipImage), "ไฟล์เกิน 8MB");
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedContentTypes.Contains(file.ContentType?.ToLowerInvariant()) || !AllowedExtensions.Contains(ext))
                ModelState.AddModelError(nameof(model.SlipImage), "ไฟล์ต้องเป็น PNG หรือ JPG เท่านั้น");
        }

        if (!ModelState.IsValid)
        {
            var invalidVm = await BuildPayViewModelAsync(invoiceId, ct);
            if (invalidVm is null) return NotFound();
            invalidVm.AmountClaimed = model.AmountClaimed;
            invalidVm.TransferredAt = model.TransferredAt;
            invalidVm.BankRefNote = model.BankRefNote;
            return View(invalidVm);
        }

        string slipUrl;
        try
        {
            slipUrl = await SaveSlipImageAsync(invoiceId, file!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Membership/Pay: slip save failed for invoice {InvoiceId}", invoiceId);
            ModelState.AddModelError(string.Empty, "บันทึกรูปสลิปไม่สำเร็จ กรุณาลองใหม่");
            var failVm = await BuildPayViewModelAsync(invoiceId, ct);
            if (failVm is null) return NotFound();
            return View(failVm);
        }

        // TransferredAt comes from the (local-time) form; persist as UTC.
        var transferredAtUtc = model.TransferredAt.ToUniversalTime();
        var submit = await _paymentSlipService.SubmitSlipAsync(
            new SubmitSlipRequest(invoiceId, userId.Value, slipUrl, model.AmountClaimed, transferredAtUtc, model.BankRefNote), ct);

        TempData[submit.Succeeded ? "MembershipOk" : "MembershipError"] =
            submit.Succeeded ? "ส่งสลิปเรียบร้อยแล้ว อยู่ระหว่างรอตรวจสอบจากทีมงาน" : (submit.Error ?? "ส่งสลิปไม่สำเร็จ");
        return RedirectToAction(nameof(Pay), new { invoiceId });
    }

    /// <summary>Most recent Issued invoice of a given fee type for a membership (the one we just created).</summary>
    private async Task<Guid?> LatestIssuedInvoiceIdAsync(Guid membershipId, FeeType feeType, CancellationToken ct)
    {
        return await _db.FeeInvoices.AsNoTracking()
            .Where(f => f.RelatedMembershipId == membershipId && f.FeeType == feeType && f.Status == FeeInvoiceStatus.Issued)
            .OrderByDescending(f => f.IssuedAtUtc)
            .Select(f => (Guid?)f.FeeInvoiceId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Build the Flow B pay page model: invoice + company bank info (ConfigVersions) + latest slip.</summary>
    private async Task<PayInvoiceViewModel?> BuildPayViewModelAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _db.FeeInvoices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FeeInvoiceId == invoiceId, ct);
        if (invoice is null) return null;

        var now = DateTime.UtcNow;
        var bank = new CompanyBankInfo
        {
            BankName = await _config.GetValueAsync(ConfigKeys.CompanyBankName, now, ct) ?? string.Empty,
            AccountNo = await _config.GetValueAsync(ConfigKeys.CompanyBankAccountNo, now, ct) ?? string.Empty,
            AccountName = await _config.GetValueAsync(ConfigKeys.CompanyBankAccountName, now, ct) ?? string.Empty,
            PromptPayId = await _config.GetValueAsync(ConfigKeys.CompanyPromptPayId, now, ct),
        };

        var slip = await _db.PaymentSlips.AsNoTracking()
            .Where(s => s.FeeInvoiceId == invoiceId)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Select(s => new SubmittedSlipInfo
            {
                PaymentSlipId = s.PaymentSlipId,
                Status = s.Status,
                AmountClaimed = s.AmountClaimed,
                SubmittedAtUtc = s.SubmittedAtUtc,
                ReviewNote = s.ReviewNote,
                SlipImageUrl = s.SlipImageUrl,
            })
            .FirstOrDefaultAsync(ct);

        return new PayInvoiceViewModel
        {
            FeeInvoiceId = invoice.FeeInvoiceId,
            Amount = invoice.Amount,
            Currency = invoice.Currency,
            FeeType = invoice.FeeType,
            InvoiceStatus = invoice.Status,
            IssuedAtUtc = invoice.IssuedAtUtc,
            Bank = bank,
            ExistingSlip = slip,
        };
    }

    /// <summary>Persist the uploaded slip under wwwroot/uploads/slips/{invoiceId}/ and return the web URL.</summary>
    private async Task<string> SaveSlipImageAsync(Guid invoiceId, Microsoft.AspNetCore.Http.IFormFile file, CancellationToken ct)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var relativeDir = Path.Combine("uploads", "slips", invoiceId.ToString("N"));
        var absoluteDir = Path.Combine(webRoot, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var absolutePath = Path.Combine(absoluteDir, fileName);
        await using (var stream = System.IO.File.Create(absolutePath))
            await file.CopyToAsync(stream, ct);

        return "/" + Path.Combine(relativeDir, fileName).Replace('\\', '/');
    }

    /// <summary>Authorization gate: the signed-in user must own the membership row they act on.</summary>
    private async Task<bool> OwnsMembershipAsync(Guid membershipId, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return false;
        return await _db.Memberships.AsNoTracking()
            .AnyAsync(m => m.MembershipId == membershipId && m.UserId == userId.Value, ct);
    }

    /// <summary>M2 gate: true only if the signed-in user currently holds a VERIFIED (non-expired) e-KYC.</summary>
    private async Task<bool> IsKycVerifiedAsync(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return false;
        var status = await _kycService.GetStatusAsync(userId.Value, ct);
        return status.Succeeded && status.Value is { StatusCode: "VERIFIED" };
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
