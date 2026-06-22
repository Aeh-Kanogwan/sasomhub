using Marketplace.Application.Auctions;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// FR-12: closes auctions whose EndAtUtc has passed. NFR-PF2: must process within 5s of close time.
/// Picks a winner that passes anti-shill (FR-11), sets Auction=Closed, opens no-touch transfer flow (FR-18).
/// Skeleton — sweep loop only.
/// </summary>
public class AuctionCloserService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5); // NFR-PF2
    private readonly IServiceProvider _services;
    private readonly ILogger<AuctionCloserService> _logger;

    public AuctionCloserService(IServiceProvider services, ILogger<AuctionCloserService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
                var auctions = scope.ServiceProvider.GetRequiredService<IAuctionService>();

                var now = DateTime.UtcNow;
                // Candidate auctions: Open and past end time (uses IX_Auctions_Status_End).
                var due = await db.Auctions
                    .Where(a => a.Status == Domain.Enums.AuctionStatus.Open && a.EndAtUtc <= now)
                    .Select(a => a.AuctionId)
                    .ToListAsync(stoppingToken);

                // FR-12: close each due auction. AuctionService excludes shill bids, sets the winner +
                // Status=Closed, opens the no-touch transfer record (FR-18), notifies the winner/seller
                // (Notification rows) and writes the append-only close AuditLog (FR-24). Idempotent: a
                // re-run on an already-Closed auction is a no-op (no duplicate notice/audit).
                var closed = 0;
                foreach (var auctionId in due)
                {
                    var result = await auctions.CloseAuctionAsync(auctionId, stoppingToken);
                    if (result.Succeeded) closed++;
                    else _logger.LogError("AuctionCloser: closing {AuctionId} failed: {Error}", auctionId, result.Error);
                }
                if (due.Count > 0)
                    _logger.LogInformation("AuctionCloser: {Closed}/{Count} due auctions closed.", closed, due.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuctionCloser sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
