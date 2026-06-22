namespace Marketplace.Web.Models.Catalog;

/// <summary>
/// Single trading-card tile used across Home/Explore/Profile galleries and Auction "more live".
/// Maps to the prototype `.tcard` component (rarity border, refraction, grade badge, serial).
/// Source: Product + ProductImages + Auction snapshot (TODO: backend-dev to project this from EF).
/// </summary>
public class ListingCardViewModel
{
    public Guid ProductId { get; init; }
    public string Title { get; init; } = string.Empty;

    /// <summary>Set / series label e.g. "Base Set · 1999".</summary>
    public string? SetLabel { get; init; }

    /// <summary>Rarity slug: common | rare | epic | legendary (drives `.r-*` CSS class).</summary>
    public string Rarity { get; init; } = "common";

    /// <summary>Appraiser grade label e.g. "PSA 10" (display only — not a platform guarantee, FR-07).</summary>
    public string? GradeLabel { get; init; }

    /// <summary>Asking price (FixedPrice) or current high bid for auctions. Formatted as ฿ in the view.</summary>
    public decimal? Price { get; init; }

    public string? SerialLabel { get; init; }

    /// <summary>Optional image URL; the prototype falls back to an emoji glyph when null.</summary>
    public string? ImageUrl { get; init; }
    public string? Glyph { get; init; }

    /// <summary>True when this card is a live auction (renders the 🔴 LIVE serial line).</summary>
    public bool IsLiveAuction { get; init; }
}
