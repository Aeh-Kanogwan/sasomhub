using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marketplace.Application.Auctions;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Auctions;
using Marketplace.Web.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-09..FR-12: live auctions. Viewing is Guest-accessible; placing a bid requires an ACTIVE
/// membership (FR-10) and runs anti-shill detection (FR-11) inside IAuctionService.
/// </summary>
public class AuctionsController : Controller
{
    private const int MoreLiveCount = 4;
    private const int BidHistoryCount = 10;

    // TempData keys consumed by the Detail view to surface the bid outcome (FR-11).
    private const string BidResultKey = "BidResult";       // accepted | outbid | flagged | error
    private const string BidMessageKey = "BidMessage";     // human-readable Thai message

    private readonly IAuctionService _auctionService;
    private readonly MarketplaceDbContext _db;
    private readonly ILogger<AuctionsController> _logger;

    public AuctionsController(IAuctionService auctionService, MarketplaceDbContext db, ILogger<AuctionsController> logger)
    {
        _auctionService = auctionService;
        _db = db;
        _logger = logger;
    }

    /// <summary>GET /Auctions/Detail/{id} — auction detail (countdown, current bid, history). Guest OK.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(Guid? id, CancellationToken ct)
    {
        AuctionDetailViewModel? vm = null;

        try
        {
            // When no id is supplied, feature the soonest-ending open auction.
            var auction = await ResolveAuctionAsync(id, ct);
            if (auction is not null)
                vm = await BuildDetailAsync(auction, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auctions/Detail: load failed for {AuctionId}; rendering sample fallback.", id);
        }

        return View(vm ?? new AuctionDetailViewModel { AuctionId = id ?? Guid.Empty });
    }

    /// <summary>Convenience landing for the navbar "ประมูลสด" link -> the featured/first open auction.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        Guid? featuredId = null;
        try
        {
            featuredId = await _db.Auctions.AsNoTracking()
                .Where(a => a.Status == AuctionStatus.Open)
                .OrderBy(a => a.EndAtUtc)
                .Select(a => (Guid?)a.AuctionId)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auctions/Index: featured auction lookup failed.");
        }

        return RedirectToAction(nameof(Detail), new { id = featuredId });
    }

    /// <summary>POST /Auctions/PlaceBid — FR-10. ACTIVE membership required; calls IAuctionService.PlaceBidAsync.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "MembershipActive")]
    public async Task<IActionResult> PlaceBid(PlaceBidViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData[BidResultKey] = "error";
            TempData[BidMessageKey] = "จำนวนเงินที่เสนอไม่ถูกต้อง";
            return RedirectToAction(nameof(Detail), new { id = model.AuctionId });
        }

        var bidderId = GetUserId();
        if (bidderId is null)
        {
            TempData[BidResultKey] = "error";
            TempData[BidMessageKey] = "ไม่พบบัญชีผู้ใช้ กรุณาเข้าสู่ระบบใหม่";
            return RedirectToAction(nameof(Detail), new { id = model.AuctionId });
        }

        // FR-11: compute IP/device hashes server-side (never trust client-posted values).
        var request = new PlaceBidRequest(
            model.AuctionId,
            bidderId.Value,
            model.Amount,
            HashOrNull(HttpContext.Connection.RemoteIpAddress?.ToString()),
            HashOrNull(Request.Headers.UserAgent.ToString()));

        try
        {
            var result = await _auctionService.PlaceBidAsync(request, ct);
            if (!result.Succeeded)
            {
                // Service rejected (too low / not open / membership). Surface the reason.
                TempData[BidResultKey] = "error";
                TempData[BidMessageKey] = result.Error ?? "ไม่สามารถเสนอราคาได้";
            }
            else if (result.Value!.Flagged)
            {
                // FR-11: accepted but flagged for shill review — excluded from winner selection.
                TempData[BidResultKey] = "flagged";
                TempData[BidMessageKey] =
                    "บันทึกการเสนอราคาแล้ว แต่ระบบตรวจพบรูปแบบที่อาจเป็นการปั่นราคา (anti-shill) " +
                    "การเสนอนี้จะถูกตรวจสอบและอาจไม่ถูกนับในการคัดเลือกผู้ชนะ (FR-11)";
            }
            else
            {
                TempData[BidResultKey] = "accepted";
                TempData[BidMessageKey] = $"เสนอราคาสำเร็จ! ราคาสูงสุดปัจจุบัน ฿{result.Value.NewHighBid:#,##0}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auctions/PlaceBid: service call failed for auction {AuctionId}.", model.AuctionId);
            TempData[BidResultKey] = "error";
            TempData[BidMessageKey] = "ขณะนี้ยังไม่สามารถเสนอราคาได้ (ระบบยังไม่พร้อม) กรุณาลองใหม่ภายหลัง";
        }

        return RedirectToAction(nameof(Detail), new { id = model.AuctionId });
    }

    // ---- helpers ----

    private async Task<Auction?> ResolveAuctionAsync(Guid? id, CancellationToken ct)
    {
        var query = _db.Auctions.AsNoTracking()
            .Include(a => a.Product).ThenInclude(p => p.Category)
            .Include(a => a.Product).ThenInclude(p => p.Images)
            .AsQueryable();

        if (id is Guid auctionId && auctionId != Guid.Empty)
            return await query.FirstOrDefaultAsync(a => a.AuctionId == auctionId, ct);

        return await query
            .Where(a => a.Status == AuctionStatus.Open)
            .OrderBy(a => a.EndAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<AuctionDetailViewModel> BuildDetailAsync(Auction auction, CancellationToken ct)
    {
        var currentHigh = auction.CurrentHighBid ?? 0m;
        var nextMin = auction.CurrentHighBid.HasValue
            ? auction.CurrentHighBid.Value + auction.BidIncrement
            : auction.StartingPrice;

        // Bid history (display name only — no PII). Highest = current high.
        var bids = await _db.Bids.AsNoTracking()
            .Where(b => b.AuctionId == auction.AuctionId
                        && (b.Status == BidStatus.Active || b.Status == BidStatus.Outbid || b.Status == BidStatus.Won))
            .OrderByDescending(b => b.Amount)
            .ThenByDescending(b => b.PlacedAtUtc)
            .Take(BidHistoryCount)
            .Select(b => new { b.Amount, DisplayName = b.Bidder.Profile!.DisplayName })
            .ToListAsync(ct);

        var history = bids
            .Select((b, idx) => new BidHistoryRow(
                string.IsNullOrWhiteSpace(b.DisplayName) ? "ผู้เสนอราคา" : b.DisplayName,
                b.Amount,
                idx == 0))
            .ToList();

        // "More live" grid: other open auctions ending soon.
        var moreLiveAuctions = await _db.Auctions.AsNoTracking()
            .Where(a => a.AuctionId != auction.AuctionId && a.Status == AuctionStatus.Open)
            .OrderBy(a => a.EndAtUtc)
            .Take(MoreLiveCount)
            .Include(a => a.Product).ThenInclude(p => p.Category)
            .Include(a => a.Product).ThenInclude(p => p.Images)
            .ToListAsync(ct);

        var moreLive = moreLiveAuctions
            .Select(a => CatalogProjection.ToCard(a.Product, a.CurrentHighBid))
            .ToList();

        return new AuctionDetailViewModel
        {
            AuctionId = auction.AuctionId,
            Card = CatalogProjection.ToCard(auction.Product, auction.CurrentHighBid),
            EndsAtUtc = auction.EndAtUtc,
            CurrentHighBid = currentHigh,
            BidIncrement = auction.BidIncrement,
            StartingPrice = auction.StartingPrice,
            NextMinimumBid = nextMin,
            BidHistory = history,
            MoreLive = moreLive
        };
    }

    private static byte[]? HashOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
