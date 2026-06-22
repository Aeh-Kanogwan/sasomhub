namespace Marketplace.Web.Models.Catalog;

/// <summary>
/// FR-06/FR-07/FR-16/FR-17: single listing detail (specs, seller trust, price history,
/// independent appraiser opinion + disclaimer, warn-on-deal notice).
/// </summary>
public class ListingDetailViewModel
{
    public ListingCardViewModel Card { get; init; } = new();
    public string? Description { get; init; }

    /// <summary>FR-06/FR-07: ordered product image URLs (primary first) from ProductImages.
    /// Empty when the listing has no uploaded photos / DB unavailable — the view falls back to a glyph.</summary>
    public IReadOnlyList<string> ImageUrls { get; init; } = new List<string>();

    public IReadOnlyList<SpecRow> Specs { get; init; } = new List<SpecRow>();

    public SellerSummary Seller { get; init; } = new();

    /// <summary>FR-16: mono price-history chart + table rows.</summary>
    public IReadOnlyList<PriceHistoryRow> PriceHistory { get; init; } = new List<PriceHistoryRow>();

    /// <summary>FR-07/FR-34: independent appraiser opinions (opinion text MUST avoid guarantee wording).</summary>
    public IReadOnlyList<AppraisalOpinionViewModel> Appraisals { get; init; } = new List<AppraisalOpinionViewModel>();

    /// <summary>True when the listing is sold via auction (renders the "เสนอราคา" CTA -> Auctions).</summary>
    public bool IsAuction { get; init; }
    public Guid? AuctionId { get; init; }

    /// <summary>FR-17: neutral warn-on-deal text shown ONLY to the viewer when the counterparty is blacklisted.
    /// Null = no warning. Never expose blacklist details publicly.</summary>
    public string? WarnOnDealMessage { get; init; }
}

public record SpecRow(string Key, string Value);

public class SellerSummary
{
    public Guid SellerId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool IsVerified { get; init; }
    public int TrustScore { get; init; }
    public int? CollectorLevel { get; init; }
    public int CompletedDeals { get; init; }
}

public record PriceHistoryRow(DateTime Date, string Grade, decimal Price, decimal ChangePercent);
