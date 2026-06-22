using Marketplace.Web.Models.Catalog;

namespace Marketplace.Web.Models.Auctions;

/// <summary>
/// FR-09..FR-12: live auction detail (countdown, current bid, bid history, anti-shill notice).
/// Bidding is reserved for ACTIVE members ([Authorize(Policy="MembershipActive")], FR-10).
/// </summary>
public class AuctionDetailViewModel
{
    public Guid AuctionId { get; init; }
    public ListingCardViewModel Card { get; init; } = new();

    public DateTime EndsAtUtc { get; init; }
    public decimal CurrentHighBid { get; init; }
    public decimal BidIncrement { get; init; }
    public decimal StartingPrice { get; init; }

    /// <summary>Suggested next bid = CurrentHighBid + BidIncrement (precomputed for the input default).</summary>
    public decimal NextMinimumBid { get; init; }

    public IReadOnlyList<BidHistoryRow> BidHistory { get; init; } = new List<BidHistoryRow>();

    /// <summary>Other open auctions for the "ประมูลอื่นที่กำลังเปิด" grid.</summary>
    public IReadOnlyList<ListingCardViewModel> MoreLive { get; init; } = new List<ListingCardViewModel>();
}

/// <summary>One row in the bid history list. Bidder shown by display name only (no PII).</summary>
public record BidHistoryRow(string BidderDisplayName, decimal Amount, bool IsCurrentHigh);
