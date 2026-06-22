namespace Marketplace.Web.Models.Catalog;

/// <summary>
/// FR-08: public browse/search (Guest allowed) — Active listings only.
/// Holds the result grid plus the current filter state echoed back into the sidebar.
/// </summary>
public class ExploreViewModel
{
    public IReadOnlyList<ListingCardViewModel> Results { get; init; } = new List<ListingCardViewModel>();
    public int TotalCount { get; init; }
    public ExploreFilter Filter { get; init; } = new();
}

/// <summary>Filter/sort state bound from the explore sidebar (rarity, grade, price, set, sort).</summary>
public class ExploreFilter
{
    public string? Query { get; init; }
    public IReadOnlyList<string> Rarities { get; init; } = new List<string>();
    public IReadOnlyList<string> Grades { get; init; } = new List<string>();
    public decimal? MaxPrice { get; init; }
    public IReadOnlyList<string> Sets { get; init; } = new List<string>();

    /// <summary>newest | price_asc | price_desc | rating — keep in sync with the select options.</summary>
    public string Sort { get; init; } = "newest";
}
