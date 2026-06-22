using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;

namespace Marketplace.Web.Models.Catalog;

/// <summary>
/// Maps catalog domain entities to the card/grid view models shared across Home/Explore/Auction.
///
/// SCHEMA NOTE (backend-dev): the prototype card surface has Rarity / SetLabel / SerialLabel concepts
/// that the current dbo.Products schema does NOT carry as first-class columns (it has Title,
/// Description, ConditionGrade, CategoryId, FixedPrice, Currency, ListingType, Status). We therefore
/// project the closest real signal:
///   - Price       = FixedPrice (or auction CurrentHighBid for auctions)
///   - GradeLabel  = ConditionGrade (appraiser opinion label — NOT a platform guarantee, FR-07)
///   - SetLabel    = Category.Name
///   - SerialLabel = Product.SerialLabel (display-only, nullable)
///   - Rarity      = Product.Rarity (common/rare/epic/legendary; CHECK-constrained column)
/// Rarity now binds to a real column, so Explore can filter on it (see ExploreController).
/// </summary>
public static class CatalogProjection
{
    public static ListingCardViewModel ToCard(Product p, decimal? auctionHighBid = null)
    {
        var isAuction = p.ListingType == ListingType.Auction;
        var primaryImage = p.Images
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.SortOrder)
            .Select(i => i.Url)
            .FirstOrDefault();

        return new ListingCardViewModel
        {
            ProductId = p.ProductId,
            Title = p.Title,
            SetLabel = p.Category?.Name,
            Rarity = string.IsNullOrWhiteSpace(p.Rarity) ? "common" : p.Rarity,
            GradeLabel = string.IsNullOrWhiteSpace(p.ConditionGrade) ? null : p.ConditionGrade,
            Price = isAuction ? auctionHighBid ?? p.FixedPrice : p.FixedPrice,
            SerialLabel = string.IsNullOrWhiteSpace(p.SerialLabel) ? null : p.SerialLabel,
            ImageUrl = primaryImage,
            Glyph = null,
            IsLiveAuction = isAuction && p.Auction is { Status: AuctionStatus.Open }
        };
    }
}
