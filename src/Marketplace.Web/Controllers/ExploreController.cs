using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>FR-08: public browse/search of Active listings. Guest-accessible (browse only).</summary>
[AllowAnonymous]
public class ExploreController : Controller
{
    private const int PageSize = 60;

    private readonly MarketplaceDbContext _db;
    private readonly ILogger<ExploreController> _logger;

    public ExploreController(MarketplaceDbContext db, ILogger<ExploreController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>GET /Explore — filtered/sorted listing grid over Active products (FR-08). Guests may browse.</summary>
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ExploreFilter? filter, CancellationToken ct)
    {
        filter ??= new ExploreFilter();

        var results = new List<ListingCardViewModel>();
        var total = 0;

        try
        {
            // Base set: Active, non-deleted listings only (FR-08). Guests reach this anonymously.
            IQueryable<Product> q = _db.Products.AsNoTracking()
                .Where(p => !p.IsDeleted && p.Status == ProductStatus.Active);

            // Free-text query across Title (and Category name for "ชุด/ผู้ขาย" style search).
            if (!string.IsNullOrWhiteSpace(filter.Query))
            {
                var term = filter.Query.Trim();
                q = q.Where(p => p.Title.Contains(term) || p.Category.Name.Contains(term));
            }

            // Price ceiling (FixedPrice). Auction-only listings (FixedPrice null) are kept when no ceiling set.
            if (filter.MaxPrice is decimal maxPrice)
                q = q.Where(p => p.FixedPrice != null && p.FixedPrice <= maxPrice);

            // Set filter maps to Category.Name (closest real column; see CatalogProjection schema note).
            if (filter.Sets.Count > 0)
            {
                var sets = filter.Sets.ToList();
                q = q.Where(p => sets.Contains(p.Category.Name));
            }

            // Grade filter maps to ConditionGrade (appraiser label, not a guarantee — FR-07).
            if (filter.Grades.Count > 0)
            {
                var grades = filter.Grades.ToList();
                q = q.Where(p => grades.Contains(p.ConditionGrade));
            }

            // Rarity facet maps directly to the Products.Rarity column (common/rare/epic/legendary).
            // Normalize to lowercase to match the CHECK-constrained slugs regardless of how the box was posted.
            if (filter.Rarities.Count > 0)
            {
                var rarities = filter.Rarities
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Select(r => r.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();
                if (rarities.Count > 0)
                    q = q.Where(p => rarities.Contains(p.Rarity));
            }

            total = await q.CountAsync(ct);

            // Sort. "rating" (seller TrustScore) needs a join not modeled on the card today, so it
            // falls back to newest until a rating projection exists.
            q = filter.Sort switch
            {
                "price_asc" => q.OrderBy(p => p.FixedPrice ?? decimal.MaxValue).ThenByDescending(p => p.CreatedAtUtc),
                "price_desc" => q.OrderByDescending(p => p.FixedPrice ?? 0m).ThenByDescending(p => p.CreatedAtUtc),
                _ => q.OrderByDescending(p => p.CreatedAtUtc),
            };

            var products = await q
                .Take(PageSize)
                .Include(p => p.Category)
                .Include(p => p.Images)
                .Include(p => p.Auction)
                .ToListAsync(ct);

            results = products.Select(p => CatalogProjection.ToCard(p)).ToList();
        }
        catch (Exception ex)
        {
            // DB not connected yet / transient error: render with empty results (view shows sample grid).
            _logger.LogWarning(ex, "Explore/Index: catalog query failed; rendering empty results (sample fallback).");
        }

        return View(new ExploreViewModel
        {
            Filter = filter,
            Results = results,
            TotalCount = total
        });
    }
}
