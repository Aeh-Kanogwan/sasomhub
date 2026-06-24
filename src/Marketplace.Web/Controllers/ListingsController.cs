using System.Security.Claims;
using Marketplace.Application.Credits;
using Marketplace.Application.Reputation;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-06/FR-07/FR-30: listing detail (Guest can view) + create listing (ACTIVE members only) + promote.
/// Mirrors the API's ListingsController responsibilities for the server-rendered site.
/// </summary>
public class ListingsController : Controller
{
    // FR-06: image upload constraints (PNG/JPG, ≤8MB each).
    private const long MaxImageBytes = 8 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = { "image/png", "image/jpeg" };
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };

    // FR-08: rarity slugs accepted on create (must match CK_Products_Rarity).
    private static readonly string[] AllowedRarities = { "common", "rare", "epic", "legendary" };

    // FR-30 promotion debits credit via ICreditService.SpendAsync (idempotent + atomic, FR-33).
    private readonly ICreditService _creditService;
    // FR-17 warn-on-deal: surface the seller's active CONFIRMED warnings (code/severity only) (M5).
    private readonly IBlacklistService _blacklist;
    private readonly MarketplaceDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ListingsController> _logger;

    public ListingsController(
        ICreditService creditService,
        IBlacklistService blacklist,
        MarketplaceDbContext db,
        IWebHostEnvironment env,
        ILogger<ListingsController> logger)
    {
        _creditService = creditService;
        _blacklist = blacklist;
        _db = db;
        _env = env;
        _logger = logger;
    }

    /// <summary>GET /Listings/Detail/{id} — single listing (specs, seller trust, images, appraisal). Guest OK (FR-07/FR-16).</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        ListingDetailViewModel? vm = null;

        try
        {
            var product = await _db.Products.AsNoTracking()
                .Where(p => p.ProductId == id && !p.IsDeleted)
                .Include(p => p.Category)
                .Include(p => p.Images)
                .Include(p => p.Auction)
                .Include(p => p.Seller).ThenInclude(s => s.Profile)
                .Include(p => p.Seller).ThenInclude(s => s.TrustScore)
                .FirstOrDefaultAsync(ct);

            if (product is not null)
            {
                var imageUrls = product.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.Url)
                    .ToList();

                // FR-07/FR-34: published independent appraiser opinions (opinions only — no guarantee).
                var appraisals = await _db.AppraisalOpinions.AsNoTracking()
                    .Where(a => a.ProductId == id && a.IsPublished)
                    .OrderByDescending(a => a.CreatedAtUtc)
                    .Select(a => new AppraisalOpinionViewModel
                    {
                        AppraisalOpinionId = a.AppraisalOpinionId,
                        AppraiserName = a.AppraiserName,
                        OpinionText = a.OpinionText,
                        DisclaimerVersion = a.DisclaimerVersion,
                        CreatedAtUtc = a.CreatedAtUtc
                    })
                    .ToListAsync(ct);

                // Completed deals = confirmed transactions where this user is the seller (FR-16).
                var completedDeals = await _db.Transactions.AsNoTracking()
                    .CountAsync(t => t.SellerId == product.SellerId && t.Status == TransactionStatus.Confirmed, ct);

                var isAuction = product.ListingType == ListingType.Auction && product.Auction is not null;

                // FR-17 warn-on-deal: if the seller (counterparty) has active CONFIRMED warnings, surface a
                // NEUTRAL banner to the viewer. Code/severity only (Legal #3) — never the internal note;
                // we render only a generic caution + the standard reason label(s). Fail-soft: any error
                // leaves the message null so the listing still loads.
                string? warnOnDeal = null;
                try
                {
                    var warnings = await _blacklist.GetActiveWarningsAsync(product.SellerId, ct);
                    if (warnings.Succeeded && warnings.Value!.Count > 0)
                    {
                        var reasons = string.Join(", ", warnings.Value.Select(w => w.ReasonDisplayName).Distinct());
                        warnOnDeal = $"โปรดใช้ความระมัดระวังในการทำธุรกรรมกับผู้ขายรายนี้ " +
                                     $"(มีประวัติที่อยู่ระหว่างการติดตามภายในระบบ: {reasons}) " +
                                     $"ระบบเป็นเพียงสื่อกลาง โปรดตรวจสอบก่อนตัดสินใจ";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Listings/Detail: warn-on-deal lookup failed for seller {SellerId}.", product.SellerId);
                }

                // FR-07/FR-08: surface real card specs (rarity/grade/serial/set). Grade is an appraiser
                // opinion label, never a platform guarantee. Omit rows we have no value for.
                var specs = new List<SpecRow>();
                if (!string.IsNullOrWhiteSpace(product.Rarity))
                    specs.Add(new SpecRow("ความหายาก", RarityLabel(product.Rarity)));
                if (!string.IsNullOrWhiteSpace(product.ConditionGrade))
                    specs.Add(new SpecRow("เกรด", product.ConditionGrade));
                if (!string.IsNullOrWhiteSpace(product.SerialLabel))
                    specs.Add(new SpecRow("ซีเรียล", product.SerialLabel!));
                if (!string.IsNullOrWhiteSpace(product.Category?.Name))
                    specs.Add(new SpecRow("ชุด / หมวด", product.Category!.Name));

                vm = new ListingDetailViewModel
                {
                    Card = CatalogProjection.ToCard(product, product.Auction?.CurrentHighBid),
                    Description = product.Description,
                    ImageUrls = imageUrls,
                    Specs = specs,
                    Appraisals = appraisals,
                    IsAuction = isAuction,
                    AuctionId = product.Auction?.AuctionId,
                    Seller = new SellerSummary
                    {
                        SellerId = product.SellerId,
                        DisplayName = product.Seller?.Profile?.DisplayName ?? "ผู้ขาย",
                        IsVerified = false, // KYC/verified projection owned by the account/membership agent.
                        TrustScore = product.Seller?.TrustScore?.Score ?? 0,
                        CollectorLevel = null,
                        CompletedDeals = completedDeals
                    },
                    // FR-17 (M5): neutral warn-on-deal banner — code/severity-derived label only, no free text.
                    WarnOnDealMessage = warnOnDeal
                };
            }
        }
        catch (Exception ex)
        {
            // DB not connected yet / transient error: fall through to the sample-fallback view.
            _logger.LogWarning(ex, "Listings/Detail: load failed for {ProductId}; rendering sample fallback.", id);
        }

        // Empty Card.Title (no product / DB down) -> the view renders its prototype sample (incl. the
        // appraisal disclaimer which it always keeps). Carry the id so links still resolve.
        return View(vm ?? new ListingDetailViewModel { Card = new ListingCardViewModel { ProductId = id } });
    }

    /// <summary>GET /Listings/Create — create-listing form. FR-06: ACTIVE membership required.</summary>
    [HttpGet]
    [Authorize(Policy = "MembershipActive")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var vm = new CreateListingViewModel { PromotionOptions = await LoadPromotionOptionsAsync(ct) };
        return View(vm);
    }

    /// <summary>POST /Listings/Create — validate + create Product (Active) + persist images + promote (FR-06/FR-30).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "MembershipActive")]
    public async Task<IActionResult> Create(CreateListingViewModel model, CancellationToken ct)
    {
        // FR-06: at least one photo is required to publish a listing.
        if (model.Images.Count == 0)
            ModelState.AddModelError(nameof(model.Images), "ต้องอัปโหลดรูปการ์ดอย่างน้อย 1 รูปก่อนเผยแพร่ (FR-06)");

        // Validate each upload: PNG/JPG only, ≤8MB each (reject unsupported types/oversized files).
        foreach (var file in model.Images)
        {
            if (file.Length <= 0)
            {
                ModelState.AddModelError(nameof(model.Images), $"ไฟล์ \"{file.FileName}\" ว่างเปล่า");
                continue;
            }
            if (file.Length > MaxImageBytes)
                ModelState.AddModelError(nameof(model.Images), $"ไฟล์ \"{file.FileName}\" เกิน 8MB");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedContentTypes.Contains(file.ContentType?.ToLowerInvariant()) || !AllowedExtensions.Contains(ext))
                ModelState.AddModelError(nameof(model.Images), $"ไฟล์ \"{file.FileName}\" ต้องเป็น PNG หรือ JPG เท่านั้น");
        }

        // Normalize rarity to a known slug; reject anything outside the CHECK set (guards against tampering).
        var rarity = (model.Rarity ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedRarities.Contains(rarity))
            ModelState.AddModelError(nameof(model.Rarity), "ระดับความหายากไม่ถูกต้อง (common/rare/epic/legendary)");

        if (!ModelState.IsValid)
        {
            model.PromotionOptions = await LoadPromotionOptionsAsync(ct);
            return View(model);
        }

        var sellerId = GetUserId();
        if (sellerId is null)
        {
            ModelState.AddModelError(string.Empty, "ไม่พบบัญชีผู้ใช้ กรุณาเข้าสู่ระบบใหม่");
            model.PromotionOptions = await LoadPromotionOptionsAsync(ct);
            return View(model);
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            SellerId = sellerId.Value,
            CategoryId = model.CategoryId ?? await DefaultCategoryIdAsync(ct),
            Title = model.Title,
            Description = model.Description,
            // GradeLabel is an appraiser opinion (FR-07) — stored as ConditionGrade (closest column).
            ConditionGrade = string.IsNullOrWhiteSpace(model.GradeLabel) ? "Used" : model.GradeLabel!,
            Rarity = rarity,
            SerialLabel = string.IsNullOrWhiteSpace(model.SerialLabel) ? null : model.SerialLabel!.Trim(),
            ListingType = model.ListingType,
            FixedPrice = model.ListingType == ListingType.FixedPrice ? model.FixedPrice : null,
            Currency = "THB",
            Status = ProductStatus.Active, // FR-06: publish (≥1 image already enforced above).
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            _db.Products.Add(product);
            await _db.SaveChangesAsync(ct); // assigns ProductId (NEWSEQUENTIALID) for the image folder.

            // Persist uploaded photos to wwwroot/uploads/listings/{productId}/ + ProductImages rows.
            var saved = await SaveImagesAsync(product.ProductId, model.Images, ct);
            await _db.SaveChangesAsync(ct);

            // FR-30: pay for any selected promotion packages with non-cashable platform credit.
            await ApplyPromotionsAsync(product.ProductId, sellerId.Value, model.SelectedPromotionPackageIds, ct);

            _logger.LogInformation("Listing {ProductId} created by {SellerId} with {ImageCount} image(s).",
                product.ProductId, sellerId, saved);

            return RedirectToAction(nameof(Detail), new { id = product.ProductId });
        }
        catch (Exception ex)
        {
            // DB not connected yet / transient error: surface inline, keep the form populated.
            _logger.LogError(ex, "Listings/Create: persist failed for seller {SellerId}.", sellerId);
            ModelState.AddModelError(string.Empty, "ขณะนี้ยังไม่สามารถบันทึกประกาศได้ (ระบบฐานข้อมูลยังไม่พร้อม) กรุณาลองใหม่ภายหลัง");
            model.PromotionOptions = await LoadPromotionOptionsAsync(ct);
            return View(model);
        }
    }

    // ---- helpers ----

    /// <summary>Persist uploaded files under wwwroot/uploads/listings/{productId}/ and add ProductImages rows.</summary>
    private async Task<int> SaveImagesAsync(Guid productId, IReadOnlyList<Microsoft.AspNetCore.Http.IFormFile> images, CancellationToken ct)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var relativeDir = Path.Combine("uploads", "listings", productId.ToString("N"));
        var absoluteDir = Path.Combine(webRoot, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        var order = 0;
        foreach (var file in images)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var absolutePath = Path.Combine(absoluteDir, fileName);

            await using (var stream = System.IO.File.Create(absolutePath))
                await file.CopyToAsync(stream, ct);

            // Web-relative URL (forward slashes) for use in <img src>.
            var url = "/" + Path.Combine(relativeDir, fileName).Replace('\\', '/');
            _db.ProductImages.Add(new ProductImage
            {
                ProductId = productId,
                Url = url,
                SortOrder = order,
                IsPrimary = order == 0,
                CreatedAtUtc = DateTime.UtcNow
            });
            order++;
        }
        return order;
    }

    /// <summary>FR-30: debit credit per selected package then record a ListingPromotion. Best-effort; a
    /// failed/insufficient promotion never voids the published listing (it is an add-on purchase).</summary>
    private async Task ApplyPromotionsAsync(Guid productId, Guid userId, IReadOnlyList<byte> packageIds, CancellationToken ct)
    {
        if (packageIds.Count == 0) return;

        var packages = await _db.PromotionPackages.AsNoTracking()
            .Where(p => p.IsActive && packageIds.Contains(p.PromotionPackageId))
            .ToListAsync(ct);

        foreach (var pkg in packages)
        {
            // refId pairs the spend ledger row with this promotion (idempotency, FR-33/B-02).
            var refId = $"PromoSpend:{productId:N}:{pkg.PromotionPackageId}";
            var spend = await _creditService.SpendAsync(userId, pkg.CreditCost, refId, ct);
            if (!spend.Succeeded)
            {
                _logger.LogWarning("FR-30 promotion {Code} skipped for {ProductId}: {Error}",
                    pkg.Code, productId, spend.Error);
                continue; // insufficient credit / not connected — skip this package, keep the listing.
            }

            var now = DateTime.UtcNow;
            _db.ListingPromotions.Add(new ListingPromotion
            {
                ProductId = productId,
                UserId = userId,
                PromotionPackageId = pkg.PromotionPackageId,
                PromotionType = pkg.PromotionType,
                CreditCost = pkg.CreditCost,
                StartsAtUtc = now,
                EndsAtUtc = now.AddDays(pkg.DurationDays),
                Status = ListingPromotionStatus.Active,
                CreatedAtUtc = now
                // CreditTransactionId is set by the credit ledger; left default here as the spend
                // ledger linkage is owned by ICreditService (the spend above appended the row).
            });
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "FR-30: saving ListingPromotions failed for {ProductId}.", productId); }
    }

    private async Task<IReadOnlyList<PromotionPackageOption>> LoadPromotionOptionsAsync(CancellationToken ct)
    {
        try
        {
            return await _db.PromotionPackages.AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.CreditCost)
                .Select(p => new PromotionPackageOption(
                    p.PromotionPackageId, p.DisplayName, p.PromotionType, p.DurationDays, p.CreditCost))
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Listings/Create: promotion options load failed; rendering sample chips.");
            return new List<PromotionPackageOption>();
        }
    }

    private async Task<int> DefaultCategoryIdAsync(CancellationToken ct)
    {
        var id = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CategoryId)
            .Select(c => (int?)c.CategoryId)
            .FirstOrDefaultAsync(ct);
        return id ?? 1; // seeded "Coins & Banknotes" = 1; trading-cards = 3.
    }

    /// <summary>Display label for a rarity slug (common/rare/epic/legendary).</summary>
    private static string RarityLabel(string rarity) => rarity?.Trim().ToLowerInvariant() switch
    {
        "legendary" => "Legendary",
        "epic" => "Epic",
        "rare" => "Rare",
        _ => "Common"
    };

    private Guid? GetUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
