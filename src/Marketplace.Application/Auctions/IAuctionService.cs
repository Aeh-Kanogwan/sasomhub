using Marketplace.Application.Common;

namespace Marketplace.Application.Auctions;

// FR-09..FR-12: auction setup, bidding (membership-gated), anti-shill detection, auto-close.

public record PlaceBidRequest(
    Guid AuctionId,
    Guid BidderId,
    decimal Amount,
    byte[]? IpAddressHash,
    byte[]? DeviceFingerprintHash);

public record BidResultDto(Guid BidId, decimal NewHighBid, bool Flagged);

/// <summary>Auction + bidding service (FR-09..FR-12). Skeleton.</summary>
public interface IAuctionService
{
    /// <summary>FR-10: place a bid. Requires active membership; runs anti-shill check (FR-11).</summary>
    Task<Result<BidResultDto>> PlaceBidAsync(PlaceBidRequest request, CancellationToken ct = default);

    /// <summary>FR-12: close an auction, pick a winner that passes anti-shill, open no-touch transfer flow.</summary>
    Task<Result> CloseAuctionAsync(Guid auctionId, CancellationToken ct = default);

    /// <summary>FR-11: evaluate a bid for shill patterns (same device/IP/linked account).</summary>
    Task<Result> RunAntiShillCheckAsync(Guid bidId, CancellationToken ct = default);
}
