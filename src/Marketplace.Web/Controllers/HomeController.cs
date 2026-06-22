using System.Diagnostics;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Web.Models;
using Marketplace.Web.Models.Catalog;
using Marketplace.Web.Models.Home;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>Landing page (index): ticker, hero, free-trial promo, hot gallery, categories. Guest-accessible (FR-08).</summary>
public class HomeController : Controller
{
    private const int HotCardCount = 8;

    private readonly MarketplaceDbContext _db;
    private readonly ILogger<HomeController> _logger;

    public HomeController(MarketplaceDbContext db, ILogger<HomeController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>GET / — home. Projects hot active listings + category tiles + a market ticker from the DB.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // No-touch / guest-safe read path. The DB may be unconnected/empty during early rollout, so
        // every query degrades to empty (the views fall back to prototype sample data on empty model).
        var hotCards = new List<ListingCardViewModel>();
        var categories = new List<CategoryTile>();
        var ticker = new List<TickerItem>();

        try
        {
            // FR-08: newest Active, non-deleted listings for the "hot now" gallery.
            var products = await _db.Products.AsNoTracking()
                .Where(p => !p.IsDeleted && p.Status == ProductStatus.Active)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Take(HotCardCount)
                .Include(p => p.Category)
                .Include(p => p.Images)
                .Include(p => p.Auction)
                .ToListAsync(ct);
            hotCards = products.Select(p => CatalogProjection.ToCard(p)).ToList();

            // Category tiles with a live count of active listings.
            categories = await _db.Categories.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new CategoryTile(
                    c.CategoryId,
                    c.Name,
                    c.Slug,
                    c.Products.Count(p => !p.IsDeleted && p.Status == ProductStatus.Active)))
                .ToListAsync(ct);

            // Market ticker (FR: ราคาผันผวนการ์ดดัง): top recent active listings by price as a
            // lightweight stand-in for "top movers" until a price-history aggregate exists.
            ticker = await _db.Products.AsNoTracking()
                .Where(p => !p.IsDeleted && p.Status == ProductStatus.Active && p.FixedPrice != null)
                .OrderByDescending(p => p.FixedPrice)
                .Take(6)
                .Select(p => new TickerItem(p.Title, p.FixedPrice!.Value, 0m))
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            // DB not connected yet / transient error: render the page with sample fallbacks, never 500.
            _logger.LogWarning(ex, "Home/Index: catalog read failed; rendering with empty model (sample fallback).");
        }

        // Stash the ticker for _Layout's shared partial; null/empty => partial uses the prototype set.
        ViewData["Ticker"] = (IReadOnlyList<TickerItem>)ticker;

        return View(new HomeViewModel
        {
            HotCards = hotCards,
            Categories = categories,
            Ticker = ticker
        });
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
