using Marketplace.Application.Auctions;
using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Trades;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// FR-09..FR-12 — auction bidding, anti-shill detection, and auto-close.
///
/// Bidding gate (FR-10): bidder must be a logged-in member whose membership is currently
/// active (Trial or paid Active) AND whose account is Active. We check membership directly
/// against the DbContext (UX_Membership_LivePerUser guarantees one live row) to stay
/// decoupled from the Membership service owned by another dev.
///
/// Anti-shill (FR-11): a bid is flagged when it shares a device fingerprint / IP hash with the
/// SELLER, or with another bidder on the same auction (collusion / linked accounts). Flagged
/// bids are NOT counted when choosing the winner. Confirmation of shilling -> trust penalty is
/// handled by Admin via TrustScoreService (reason SHILL_BIDDING), not auto-applied here.
///
/// Close (FR-12): pick the highest non-shill bid that meets the reserve, set the winner, flip
/// Auction=Closed, and open a no-touch transfer record (FR-18) via ITradeService.
/// </summary>
public class AuctionService : IAuctionService
{
    private readonly MarketplaceDbContext _db;
    private readonly ITradeService _tradeService;
    private readonly IAuditService _audit;
    private readonly ILogger<AuctionService> _logger;

    public AuctionService(MarketplaceDbContext db, ITradeService tradeService, IAuditService audit, ILogger<AuctionService> logger)
    {
        _db = db;
        _tradeService = tradeService;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<BidResultDto>> PlaceBidAsync(PlaceBidRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
            return Result<BidResultDto>.Fail("Bid amount must be greater than zero.");

        var auction = await _db.Auctions
            .Include(a => a.Product)
            .FirstOrDefaultAsync(a => a.AuctionId == request.AuctionId, ct);
        if (auction is null)
            return Result<BidResultDto>.Fail("Auction not found.");

        var now = DateTime.UtcNow;
        if (auction.Status != AuctionStatus.Open)
            return Result<BidResultDto>.Fail("Auction is not open for bidding.");
        if (now < auction.StartAtUtc)
            return Result<BidResultDto>.Fail("Auction has not started yet.");
        if (now >= auction.EndAtUtc)
            return Result<BidResultDto>.Fail("Auction has already ended.");

        // Seller cannot bid on their own auction (basic anti-shill + integrity).
        if (auction.Product.SellerId == request.BidderId)
            return Result<BidResultDto>.Fail("Seller cannot bid on their own auction.");

        // FR-10 gate: bidder account active + membership active.
        var bidder = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == request.BidderId, ct);
        if (bidder is null)
            return Result<BidResultDto>.Fail("Bidder not found.");
        if (bidder.AccountStatus != AccountStatus.Active)
            return Result<BidResultDto>.Fail("Account is not active; bidding is not allowed.");
        if (!await IsMembershipActiveAsync(request.BidderId, now, ct))
            return Result<BidResultDto>.Fail("Active membership required to bid (Trial or paid). Please renew.");

        // Minimum acceptable bid (FR-10): currentHigh + increment, or starting price for the first bid.
        var minimumBid = auction.CurrentHighBid.HasValue
            ? auction.CurrentHighBid.Value + auction.BidIncrement
            : auction.StartingPrice;
        if (request.Amount < minimumBid)
            return Result<BidResultDto>.Fail(
                $"Bid must be at least {minimumBid:0.00} (current high + increment / starting price).");

        // Demote the previous high bid to Outbid (best-effort; the new bid becomes the active high).
        var previousHigh = await _db.Bids
            .Where(x => x.AuctionId == auction.AuctionId && x.Status == BidStatus.Active)
            .ToListAsync(ct);
        foreach (var b in previousHigh)
            b.Status = BidStatus.Outbid;

        var bid = new Bid
        {
            AuctionId = auction.AuctionId,
            BidderId = request.BidderId,
            Amount = request.Amount,
            Status = BidStatus.Active,
            IpAddressHash = request.IpAddressHash,
            DeviceFingerprintHash = request.DeviceFingerprintHash,
            PlacedAtUtc = now
        };

        // FR-11 anti-shill: evaluate at placement time and store the flag/relationship.
        var (flagged, relationship) = await EvaluateShillAsync(auction, bid, ct);
        bid.IsFlaggedShill = flagged;
        bid.RelationshipFlag = relationship;

        _db.Bids.Add(bid);

        // Update the displayed high bid. A flagged bid still records but does not become the
        // basis for the *winning* selection at close; for the live high-bid display we keep the
        // numeric max so the auction UI reflects what was actually bid.
        auction.CurrentHighBid = request.Amount;

        await _db.SaveChangesAsync(ct);

        if (flagged)
            _logger.LogWarning("Anti-shill: bid {BidId} on auction {AuctionId} flagged ({Rel}).",
                bid.BidId, auction.AuctionId, relationship);

        return Result<BidResultDto>.Success(new BidResultDto(bid.BidId, auction.CurrentHighBid ?? request.Amount, flagged));
    }

    public async Task<Result> CloseAuctionAsync(Guid auctionId, CancellationToken ct = default)
    {
        var auction = await _db.Auctions
            .Include(a => a.Product)
            .FirstOrDefaultAsync(a => a.AuctionId == auctionId, ct);
        if (auction is null)
            return Result.Fail("Auction not found.");
        if (auction.Status == AuctionStatus.Closed)
            return Result.Success(); // idempotent: already closed.
        if (auction.Status == AuctionStatus.Cancelled)
            return Result.Fail("Auction is cancelled.");

        var now = DateTime.UtcNow;

        // Winner = highest NON-shill bid that meets the reserve (FR-11/FR-12).
        var candidates = await _db.Bids
            .Where(b => b.AuctionId == auctionId
                        && !b.IsFlaggedShill
                        && (b.Status == BidStatus.Active || b.Status == BidStatus.Outbid))
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.PlacedAtUtc) // earliest wins ties
            .ToListAsync(ct);

        Bid? winner = null;
        foreach (var b in candidates)
        {
            if (auction.ReservePrice.HasValue && b.Amount < auction.ReservePrice.Value)
                continue; // reserve not met
            winner = b;
            break;
        }

        auction.Status = AuctionStatus.Closed;

        if (winner is null)
        {
            // No eligible winner (no bids, all shill, or reserve unmet). Close without a sale.
            auction.WinningBidId = null;
            if (auction.Product.Status == ProductStatus.Active)
                auction.Product.Status = ProductStatus.Closed;
            auction.Product.UpdatedAtUtc = now;

            // FR-24: audit the close (no sale). No winner -> only the seller is a party; AuctionClosed
            // notice is informational, in-app, milestone-less (so it is exempt from the lifecycle dedupe index).
            _audit.Write(
                action: "Auction.ClosedNoWinner",
                entityType: "Auction",
                entityId: auctionId.ToString("N"),
                actorUserId: null,
                after: new { auction.AuctionId, auction.ProductId, WinnerBidId = (Guid?)null });
            AddAuctionNotice(auction.Product.SellerId, now);

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Auction {AuctionId} closed with no winner.", auctionId);
            return Result.Success();
        }

        winner.Status = BidStatus.Won;
        auction.WinningBidId = winner.BidId;
        // Mark losing (non-won) active/outbid bids cleanly.
        var losers = candidates.Where(b => b.BidId != winner.BidId
                                           && b.Status == BidStatus.Active).ToList();
        foreach (var l in losers)
            l.Status = BidStatus.Outbid;

        auction.Product.Status = ProductStatus.Sold;
        auction.Product.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);

        // FR-18: open the no-touch transfer record (NO escrow/wallet). Separate save so the
        // winner is durably recorded even if transaction creation needs a retry.
        var txResult = await _tradeService.CreateNoTouchTransactionAsync(
            productId: auction.ProductId,
            sellerId: auction.Product.SellerId,
            buyerId: winner.BidderId,
            winningBidId: winner.BidId,
            agreedAmount: winner.Amount,
            ct: ct);

        if (!txResult.Succeeded)
        {
            // Winner is set; transfer record can be retried by the worker on the next sweep.
            _logger.LogError("Auction {AuctionId} closed (winner {BidId}) but no-touch tx creation failed: {Error}",
                auctionId, winner.BidId, txResult.Error);
            return Result.Fail($"Auction closed but transfer record failed: {txResult.Error}");
        }

        // FR-12/FR-24: audit the close-with-winner and notify both parties (winner + seller). The
        // no-touch transaction itself is audited inside TradeService. Notifications are in-app,
        // milestone-less, so they are exempt from the lifecycle dedupe index (UX_Notif_NoDup).
        _audit.Write(
            action: "Auction.ClosedWithWinner",
            entityType: "Auction",
            entityId: auctionId.ToString("N"),
            actorUserId: null,
            after: new
            {
                auction.AuctionId,
                auction.ProductId,
                WinnerBidId = winner.BidId,
                WinnerUserId = winner.BidderId,
                SellerUserId = auction.Product.SellerId,
                WinningAmount = winner.Amount,
                TransactionId = txResult.Value!.TransactionId,
            });
        AddAuctionNotice(winner.BidderId, now, won: true);
        AddAuctionNotice(auction.Product.SellerId, now, won: false);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Auction {AuctionId} closed. Winner bid {BidId}, no-touch tx {TxId}.",
            auctionId, winner.BidId, txResult.Value!.TransactionId);
        return Result.Success();
    }

    /// <summary>
    /// FR-12: stage an in-app auction-close notice for a party. Milestone-less (NULL) and
    /// membership-less so it sits outside the lifecycle dedupe index UX_Notif_NoDup; the
    /// NotificationDispatchService delivers it like any other Pending in-app notice.
    /// </summary>
    private void AddAuctionNotice(Guid userId, DateTime now, bool won = false)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = won ? NotificationType.AuctionWon : NotificationType.AuctionClosed,
            Channel = NotificationChannel.InApp,
            RelatedMembershipId = null,
            Milestone = null,
            ScheduledForUtc = now,
            Status = NotificationStatus.Pending,
            CreatedAtUtc = now,
        });
    }

    public async Task<Result> RunAntiShillCheckAsync(Guid bidId, CancellationToken ct = default)
    {
        var bid = await _db.Bids.FirstOrDefaultAsync(b => b.BidId == bidId, ct);
        if (bid is null)
            return Result.Fail("Bid not found.");

        var auction = await _db.Auctions.Include(a => a.Product)
            .FirstOrDefaultAsync(a => a.AuctionId == bid.AuctionId, ct);
        if (auction is null)
            return Result.Fail("Auction not found.");

        var (flagged, relationship) = await EvaluateShillAsync(auction, bid, ct);
        bid.IsFlaggedShill = flagged;
        bid.RelationshipFlag = relationship;
        await _db.SaveChangesAsync(ct);

        if (flagged)
            _logger.LogWarning("Anti-shill recheck: bid {BidId} flagged ({Rel}).", bidId, relationship);
        return Result.Success();
    }

    /// <summary>
    /// FR-11 heuristic: flag a bid that shares a device fingerprint or IP hash with the seller
    /// (most direct shill), or with another bidder on the same auction (collusion ring).
    /// Returns (flagged, relationshipFlag) where the flag is one of
    /// SAME_DEVICE / SAME_IP / LINKED_ACCOUNT (mirrors the Bid column comment, Legal #6).
    /// </summary>
    private async Task<(bool Flagged, string? Relationship)> EvaluateShillAsync(Auction auction, Bid bid, CancellationToken ct)
    {
        var sellerId = auction.Product.SellerId;

        // 1) Bidder colludes with the SELLER via a shared device/IP. The seller's signals come
        //    from any bids they ever placed elsewhere with the same hashes (best-effort signal set).
        if (bid.DeviceFingerprintHash is not null)
        {
            var sellerSameDevice = await _db.Bids.AsNoTracking().AnyAsync(b =>
                b.BidderId == sellerId && b.DeviceFingerprintHash == bid.DeviceFingerprintHash, ct);
            if (sellerSameDevice)
                return (true, "SAME_DEVICE");
        }
        if (bid.IpAddressHash is not null)
        {
            var sellerSameIp = await _db.Bids.AsNoTracking().AnyAsync(b =>
                b.BidderId == sellerId && b.IpAddressHash == bid.IpAddressHash, ct);
            if (sellerSameIp)
                return (true, "SAME_IP");
        }

        // 2) Another DIFFERENT bidder on THIS auction shares the same device/IP -> linked accounts.
        if (bid.DeviceFingerprintHash is not null)
        {
            var linkedDevice = await _db.Bids.AsNoTracking().AnyAsync(b =>
                b.AuctionId == auction.AuctionId
                && b.BidId != bid.BidId
                && b.BidderId != bid.BidderId
                && b.DeviceFingerprintHash == bid.DeviceFingerprintHash, ct);
            if (linkedDevice)
                return (true, "SAME_DEVICE");
        }
        if (bid.IpAddressHash is not null)
        {
            var linkedIp = await _db.Bids.AsNoTracking().AnyAsync(b =>
                b.AuctionId == auction.AuctionId
                && b.BidId != bid.BidId
                && b.BidderId != bid.BidderId
                && b.IpAddressHash == bid.IpAddressHash, ct);
            if (linkedIp)
                return (true, "SAME_IP");
        }

        // TODO(FR-11): extend with payment-hint / KYC-hash / account-graph linkage once those
        // signals are available (see SRS FR-11 "เครือข่ายบัญชี"). Flag LINKED_ACCOUNT then.
        return (false, null);
    }

    /// <summary>
    /// FR-10 membership gate, decoupled from the Membership service. Active = a live membership row
    /// (Trial within trial window, or Active within paid-through). Mirrors IsMembershipActiveAsync
    /// but without taking a dependency on that service (owned by another dev / may be unregistered
    /// in the Worker host).
    /// </summary>
    private async Task<bool> IsMembershipActiveAsync(Guid userId, DateTime now, CancellationToken ct)
    {
        return await _db.Memberships.AsNoTracking().AnyAsync(m =>
            m.UserId == userId
            && ((m.Status == MembershipStatus.Trial
                    && (m.TrialEndsAtUtc == null || m.TrialEndsAtUtc >= now))
                || (m.Status == MembershipStatus.Active
                    && (m.PaidThroughUtc == null || m.PaidThroughUtc >= now))),
            ct);
    }
}
