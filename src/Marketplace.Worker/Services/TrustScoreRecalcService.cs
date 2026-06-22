using Marketplace.Application.Reputation;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// FR-14/FR-15: periodically recalculates Trust Scores from events and applies auto-penalty rules
/// (start 100, -20 per minor offense, &lt;60 => Suspended). Permanent Ban/Blacklist always requires
/// Admin confirmation (DP-3, human-in-the-loop) — this worker only proposes. Skeleton.
/// </summary>
public class TrustScoreRecalcService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private readonly IServiceProvider _services;
    private readonly ILogger<TrustScoreRecalcService> _logger;

    public TrustScoreRecalcService(IServiceProvider services, ILogger<TrustScoreRecalcService> logger)
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
                var trust = scope.ServiceProvider.GetRequiredService<ITrustScoreService>();

                // FR-14/FR-15: re-assert the suspension rule for active users whose score dropped
                // below 60. RecalculateAsync flips Active -> Suspended only (reversible). Severe-case
                // Ban proposals (PendingBanReview) are created at event time and never auto-cleared
                // here — permanent ban always requires Admin confirmation (DP-3). History is append-only.
                var userIds = await db.TrustScores
                    .Where(t => t.Score < Marketplace.Infrastructure.Services.TrustScoreService.SuspendThreshold)
                    .Select(t => t.UserId)
                    .ToListAsync(stoppingToken);

                foreach (var userId in userIds)
                {
                    var result = await trust.RecalculateAsync(userId, stoppingToken);
                    if (!result.Succeeded)
                        _logger.LogWarning("TrustScoreRecalc: {UserId} failed: {Error}", userId, result.Error);
                }
                _logger.LogDebug("TrustScoreRecalc tick: re-asserted {Count} sub-threshold users.", userIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrustScoreRecalc sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
