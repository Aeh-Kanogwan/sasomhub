using System.Security.Claims;
using Marketplace.Application.Trades;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Trade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-18 (NO-TOUCH) / FR-19: shows the seller's bank details for a DIRECT buyer->seller transfer,
/// the "platform does not receive/hold/intermediate funds" banner, and confirm-receipt + review.
///
/// LEGAL INVARIANT (must not regress): there is NO action here that accepts money, creates a wallet,
/// holds an escrow balance, or charges a commission. The platform only displays off-platform details
/// and records status (Pending -> Transferred -> Confirmed) on Transactions via ITradeService.
/// </summary>
[Authorize]
public class TransferController : Controller
{
    private const string TransferResultKey = "TransferResult";   // ok | error
    private const string TransferMessageKey = "TransferMessage"; // Thai message

    private readonly ITradeService _tradeService;
    private readonly MarketplaceDbContext _db;
    private readonly ILogger<TransferController> _logger;

    public TransferController(ITradeService tradeService, MarketplaceDbContext db, ILogger<TransferController> logger)
    {
        _tradeService = tradeService;
        _db = db;
        _logger = logger;
    }

    /// <summary>GET /Transfer/{id} — order summary + seller bank box + no-touch banner (FR-18). Buyer-only.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(Guid id, CancellationToken ct)
    {
        TransferViewModel? vm = null;

        try
        {
            var buyerId = GetUserId();
            var tx = await _db.Transactions.AsNoTracking()
                .Where(t => t.TransactionId == id)
                .Select(t => new
                {
                    t.TransactionId,
                    t.BuyerId,
                    t.AgreedAmount,
                    t.Currency,
                    t.Status,
                    ProductTitle = t.Product.Title,
                    ProductGrade = t.Product.ConditionGrade,
                    SellerDisplayName = t.Seller.Profile!.DisplayName
                })
                .FirstOrDefaultAsync(ct);

            // FR-18: buyer-only access. Never expose another user's transfer page.
            if (tx is not null && buyerId is Guid bid && tx.BuyerId == bid)
            {
                vm = new TransferViewModel
                {
                    TransactionId = tx.TransactionId,
                    ProductTitle = tx.ProductTitle,
                    GradeLabel = tx.ProductGrade,
                    AgreedAmount = tx.AgreedAmount,
                    Currency = tx.Currency,
                    Status = tx.Status,
                    // SCHEMA NOTE: no seller bank-account entity exists in the domain yet, so the bank
                    // box is display-only with a masked placeholder. When a (PDPA-masked) bank-details
                    // store is added (owned by the account/KYC agent), project it here. NEVER a wallet/escrow.
                    SellerBank = new SellerBankInfo
                    {
                        BankName = "—",
                        AccountNumberMasked = "xxx-x-xxxxx-x",
                        AccountName = tx.SellerDisplayName ?? "ผู้ขาย"
                    }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transfer/Index: load failed for {TransactionId}; rendering sample fallback.", id);
        }

        // Empty/unauthorized -> fall back to the prototype view carrying the id (no-touch banner stays).
        return View(vm ?? new TransferViewModel { TransactionId = id });
    }

    /// <summary>POST /Transfer/MarkTransferred — buyer notes they transferred off-platform (status only, no money).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkTransferred(Guid id, CancellationToken ct)
    {
        var buyerId = GetUserId();
        if (buyerId is null)
        {
            TempData[TransferResultKey] = "error";
            TempData[TransferMessageKey] = "ไม่พบบัญชีผู้ใช้ กรุณาเข้าสู่ระบบใหม่";
            return RedirectToAction(nameof(Index), new { id });
        }

        try
        {
            // No-touch: records a status transition only; no funds are received or moved (FR-18).
            var result = await _tradeService.MarkTransferredAsync(
                new MarkTransferredRequest(id, buyerId.Value, ExternalPaymentNote: null), ct);

            TempData[TransferResultKey] = result.Succeeded ? "ok" : "error";
            TempData[TransferMessageKey] = result.Succeeded
                ? "บันทึกแล้วว่าคุณได้โอนเงินตรงให้ผู้ขาย (แพลตฟอร์มไม่รับ/ไม่ถือเงิน)"
                : result.Error ?? "ไม่สามารถบันทึกสถานะได้";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer/MarkTransferred: service call failed for {TransactionId}.", id);
            TempData[TransferResultKey] = "error";
            TempData[TransferMessageKey] = "ขณะนี้ยังไม่สามารถบันทึกสถานะได้ (ระบบยังไม่พร้อม) กรุณาลองใหม่ภายหลัง";
        }

        return RedirectToAction(nameof(Index), new { id });
    }

    /// <summary>POST /Transfer/ConfirmReceipt — FR-19: buyer confirms receipt; opens review + Trust Score update.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReceipt(Guid id, CancellationToken ct)
    {
        var buyerId = GetUserId();
        if (buyerId is null)
        {
            TempData[TransferResultKey] = "error";
            TempData[TransferMessageKey] = "ไม่พบบัญชีผู้ใช้ กรุณาเข้าสู่ระบบใหม่";
            return RedirectToAction(nameof(Index), new { id });
        }

        try
        {
            // FR-19: status Transferred->Confirmed; the service triggers the two-sided Trust Score update.
            var result = await _tradeService.ConfirmReceiptAsync(new ConfirmReceiptRequest(id, buyerId.Value), ct);

            if (result.Succeeded)
            {
                TempData[TransferResultKey] = "ok";
                TempData[TransferMessageKey] = "ยืนยันการรับการ์ดแล้ว ขอบคุณที่ทำธุรกรรมอย่างปลอดภัย (FR-19)";
                // Review form is owned by the account/reputation agent; route to the profile for now.
                return RedirectToAction("Profile", "Account");
            }

            TempData[TransferResultKey] = "error";
            TempData[TransferMessageKey] = result.Error ?? "ไม่สามารถยืนยันการรับของได้";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer/ConfirmReceipt: service call failed for {TransactionId}.", id);
            TempData[TransferResultKey] = "error";
            TempData[TransferMessageKey] = "ขณะนี้ยังไม่สามารถยืนยันได้ (ระบบยังไม่พร้อม) กรุณาลองใหม่ภายหลัง";
        }

        return RedirectToAction(nameof(Index), new { id });
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
