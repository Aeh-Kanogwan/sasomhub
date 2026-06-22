using Marketplace.Web.Models.Catalog;

namespace Marketplace.Web.Models.Home;

/// <summary>
/// Landing page: market ticker, hero, free-trial promo banner (FR-27), "hot now" gallery (FR-08),
/// and category tiles.
/// </summary>
public class HomeViewModel
{
    public IReadOnlyList<TickerItem> Ticker { get; init; } = new List<TickerItem>();
    public IReadOnlyList<ListingCardViewModel> HotCards { get; init; } = new List<ListingCardViewModel>();
    public IReadOnlyList<CategoryTile> Categories { get; init; } = new List<CategoryTile>();
}

public record TickerItem(string Symbol, decimal Price, decimal ChangePercent);
public record CategoryTile(int CategoryId, string Name, string Slug, int Count);
