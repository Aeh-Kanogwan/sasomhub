using System.ComponentModel.DataAnnotations;
using Marketplace.Application.Credits;
using Marketplace.Application.Memberships;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

/// <summary>Listing / catalog endpoints (FR-06..FR-08, FR-30).</summary>
[ApiController]
[Route("api/listings")]
public class ListingsController : ApiControllerBase
{
    private readonly MarketplaceDbContext _db;
    private readonly IMembershipService _membership;
    private readonly ICreditService _credit;
    private readonly ILogger<ListingsController> _logger;

    public ListingsController(
        MarketplaceDbContext db, IMembershipService membership, ICreditService credit,
        ILogger<ListingsController> logger)
    {
        _db = db;
        _membership = membership;
        _credit = credit;
        _logger = logger;
    }

    /// <summary>FR-08: public search/browse (Guest allowed) — Active, non-deleted listings only.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] int? categoryId, [FromQuery] string? rarity,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active && !p.IsDeleted);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => p.Title.Contains(q));
        if (categoryId is { } cat)
            query = query.Where(p => p.CategoryId == cat);
        if (!string.IsNullOrWhiteSpace(rarity))
            query = query.Where(p => p.Rarity == rarity);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ListingView(
                p.ProductId, p.SellerId, p.CategoryId, p.Title, p.ConditionGrade,
                p.Rarity, p.ListingType.ToString(), p.FixedPrice, p.Currency, p.CreatedAtUtc))
            .ToListAsync(ct);

        return Ok(new SearchResult(total, page, pageSize, items));
    }

    /// <summary>
    /// FR-06: create a listing. Requires an ACTIVE membership ("MembershipActive" policy + a DB re-check).
    /// FR-06 KYC threshold: a listing whose price is over the configured threshold requires a Verified
    /// (KYC-completed) tier — enforced here against the seller's live membership tier.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "MembershipActive")]
    public async Task<IActionResult> Create([FromBody] CreateListingRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);
        if (string.IsNullOrWhiteSpace(request.Title))
            return Problem("Title is required.");
        if (request.FixedPrice is < 0)
            return Problem("Price cannot be negative.");

        // FR-06 gate (authoritative re-check, not just the token claim): membership must be active now.
        if (!await _membership.IsMembershipActiveAsync(userId, ct))
            return Problem("An active membership is required to create a listing. Please renew.");

        var categoryExists = await _db.Categories.AsNoTracking().AnyAsync(c => c.CategoryId == request.CategoryId, ct);
        if (!categoryExists)
            return Problem("Category not found.");

        // FR-06 KYC-over-threshold: high-value listings require a KYC tier (RequiresKyc). Threshold is
        // admin-config (FR-35); default 50,000 THB until a ConfigVersions row overrides it.
        if (request.FixedPrice is { } price)
        {
            var threshold = await GetKycThresholdAsync(ct);
            if (price >= threshold)
            {
                var onKycTier = await _db.Memberships.AsNoTracking()
                    .Where(m => m.UserId == userId &&
                                (m.Status == MembershipStatus.Trial || m.Status == MembershipStatus.Active))
                    .Join(_db.MembershipTiers.AsNoTracking(),
                        m => m.MembershipTierId, t => t.MembershipTierId, (m, t) => t.RequiresKyc)
                    .FirstOrDefaultAsync(ct);
                if (!onKycTier)
                    return Problem($"Listings at or above {threshold:0} THB require KYC verification (Verified tier).");
            }
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            SellerId = userId,
            CategoryId = request.CategoryId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            ConditionGrade = string.IsNullOrWhiteSpace(request.ConditionGrade) ? "Used" : request.ConditionGrade.Trim(),
            Rarity = string.IsNullOrWhiteSpace(request.Rarity) ? "common" : request.Rarity.Trim(),
            SerialLabel = request.SerialLabel?.Trim(),
            ListingType = ListingType.FixedPrice,
            FixedPrice = request.FixedPrice,
            Currency = "THB",
            Status = ProductStatus.Active,   // FR-06: Draft -> Active after validation
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        var dto = new ListingView(
            product.ProductId, product.SellerId, product.CategoryId, product.Title, product.ConditionGrade,
            product.Rarity, product.ListingType.ToString(), product.FixedPrice, product.Currency, product.CreatedAtUtc);
        return CreatedAtAction(nameof(Search), new { id = product.ProductId }, dto);
    }

    /// <summary>
    /// FR-30: buy a credit-paid promotion slot for the caller's own listing. Spends credit (FR-33 idempotent
    /// per promotion id) then records the ListingPromotion linked to that PromoSpend ledger row (1:1, UQ).
    /// </summary>
    [HttpPost("{productId:guid}/promote")]
    [Authorize(Policy = "MembershipActive")]
    public async Task<IActionResult> Promote(Guid productId, [FromBody] PromoteRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId && !p.IsDeleted, ct);
        if (product is null)
            return Problem("Listing not found.");
        if (product.SellerId != userId)
            return Problem("You can only promote your own listing.");

        var package = await _db.PromotionPackages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PromotionPackageId == request.PromotionPackageId && p.IsActive, ct);
        if (package is null)
            return Problem("Promotion package not found.");

        // Idempotency anchor for the credit debit (FR-33). One promotion id == at most one PromoSpend row.
        var promotionId = Guid.NewGuid();
        var refId = promotionId.ToString();

        var spend = await _credit.SpendAsync(userId, package.CreditCost, refId, ct);
        if (!spend.Succeeded)
            return Problem(spend.Error); // e.g. "Insufficient credit balance." -> 400

        // Resolve the PromoSpend ledger row the debit produced (idempotency key "PromoSpend:{refId}")
        // so we can satisfy ListingPromotion.CreditTransactionId (NOT NULL + UNIQUE, FR-33 Y-08).
        var idempotencyKey = $"{CreditTransactionType.PromoSpend}:{refId}";
        var creditTxId = await _db.CreditTransactions.AsNoTracking()
            .Where(t => t.IdempotencyKey == idempotencyKey)
            .Select(t => (long?)t.CreditTransactionId)
            .FirstOrDefaultAsync(ct);
        if (creditTxId is null)
        {
            _logger.LogError("Promote: PromoSpend ledger row missing for {RefId} after a succeeded spend.", refId);
            return Problem("Promotion failed after credit debit; please retry.");
        }

        var now = DateTime.UtcNow;
        var promotion = new ListingPromotion
        {
            ListingPromotionId = promotionId,
            ProductId = productId,
            UserId = userId,
            PromotionPackageId = package.PromotionPackageId,
            PromotionType = package.PromotionType,
            CreditCost = package.CreditCost,
            StartsAtUtc = now,
            EndsAtUtc = now.AddDays(package.DurationDays),
            Status = ListingPromotionStatus.Active,
            CreditTransactionId = creditTxId.Value,
            CreatedAtUtc = now,
        };
        _db.ListingPromotions.Add(promotion);
        await _db.SaveChangesAsync(ct);

        var dto = new PromotionView(
            promotion.ListingPromotionId, promotion.ProductId, promotion.PromotionType.ToString(),
            promotion.CreditCost, promotion.StartsAtUtc, promotion.EndsAtUtc, promotion.Status.ToString());
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    /// <summary>FR-06/FR-35: KYC-required price threshold from config (default 50,000 THB).</summary>
    private async Task<decimal> GetKycThresholdAsync(CancellationToken ct)
    {
        var raw = await _db.ConfigVersions.AsNoTracking()
            .Where(c => c.ConfigKey == "Listing.KycThresholdTHB" && c.EffectiveFromUtc <= DateTime.UtcNow)
            .OrderByDescending(c => c.EffectiveFromUtc)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 50_000m;
    }

    public sealed record CreateListingRequest(
        [property: Required] string Title,
        string? Description,
        int CategoryId,
        string? ConditionGrade,
        string? Rarity,
        string? SerialLabel,
        decimal? FixedPrice);

    public sealed record PromoteRequest(byte PromotionPackageId);

    public sealed record ListingView(
        Guid ProductId, Guid SellerId, int CategoryId, string Title, string ConditionGrade,
        string Rarity, string ListingType, decimal? FixedPrice, string Currency, DateTime CreatedAtUtc);

    public sealed record SearchResult(int Total, int Page, int PageSize, IReadOnlyList<ListingView> Items);

    public sealed record PromotionView(
        Guid ListingPromotionId, Guid ProductId, string PromotionType, decimal CreditCost,
        DateTime StartsAtUtc, DateTime EndsAtUtc, string Status);
}
