using System.Security.Cryptography;
using System.Text;
using Marketplace.Application.Auctions;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

/// <summary>Bidding endpoints (FR-09..FR-12).</summary>
[ApiController]
[Route("api/bids")]
[Authorize]
public class BidsController : ApiControllerBase
{
    private readonly IAuctionService _auctions;
    private readonly MarketplaceDbContext _db;

    public BidsController(IAuctionService auctions, MarketplaceDbContext db)
    {
        _auctions = auctions;
        _db = db;
    }

    /// <summary>
    /// FR-10: place a bid. Requires an ACTIVE membership ("MembershipActive" policy gates the claim; the
    /// AuctionService re-verifies against the DB so a stale token cannot bypass it). FR-11: we hash the
    /// caller's IP + the client-supplied device fingerprint for anti-shill detection (raw values never stored).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "MembershipActive")]
    public async Task<IActionResult> Place([FromBody] PlaceBidBody body, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);

        var ipHash = HashOrNull(HttpContext.Connection.RemoteIpAddress?.ToString());
        var deviceHash = HashOrNull(body.DeviceFingerprint);

        var result = await _auctions.PlaceBidAsync(
            new PlaceBidRequest(body.AuctionId, userId, body.Amount, ipHash, deviceHash), ct);
        return FromResult(result);
    }

    /// <summary>List bids for an auction (highest first). Public read (FR-08-style transparency).</summary>
    [HttpGet("auction/{auctionId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> ByAuction(Guid auctionId, CancellationToken ct)
    {
        var auctionExists = await _db.Auctions.AsNoTracking().AnyAsync(a => a.AuctionId == auctionId, ct);
        if (!auctionExists)
            return Problem("Auction not found.");

        var rows = await _db.Bids.AsNoTracking()
            .Where(b => b.AuctionId == auctionId)
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.PlacedAtUtc)
            .Select(b => new BidView(b.BidId, b.BidderId, b.Amount, b.Status.ToString(), b.PlacedAtUtc))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>SHA-256 hash of an anti-shill signal (IP / device fingerprint). We never persist the raw value.</summary>
    private static byte[]? HashOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public sealed record PlaceBidBody(Guid AuctionId, decimal Amount, string? DeviceFingerprint);
    public sealed record BidView(Guid BidId, Guid BidderId, decimal Amount, string Status, DateTime PlacedAtUtc);
}
