using Marketplace.Application.Auditing;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// FR-30: closes listing promotions whose EndsAtUtc has passed (Status: Active -> Expired), so the
/// listing returns to its normal ranking. Each transition is written to the append-only AuditLog (FR-24).
///
/// Idempotency: only Active rows past their end are selected (uses IX_ListPromo_Active_End), so a re-run
/// after the flip is a no-op — no promotion is expired twice and no duplicate audit row is written.
/// This only changes a promotion's lifecycle Status; it never touches credit (the spend was charged
/// up-front when the promotion was created and is non-refundable, no-touch revenue model).
/// </summary>
public class ListingPromotionExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int BatchSize = 200;
    private readonly IServiceProvider _services;
    private readonly ILogger<ListingPromotionExpiryService> _logger;

    public ListingPromotionExpiryService(IServiceProvider services, ILogger<ListingPromotionExpiryService> logger)
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
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListingPromotionExpiry sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>One sweep: flip lapsed Active promotions to Expired + audit. Idempotent. Returns count expired.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var now = DateTime.UtcNow;

        var lapsed = await db.ListingPromotions
            .Where(p => p.Status == ListingPromotionStatus.Active && p.EndsAtUtc <= now)
            .OrderBy(p => p.EndsAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var promo in lapsed)
        {
            promo.Status = ListingPromotionStatus.Expired;

            // FR-24: append-only audit of the promotion expiry (system actor).
            audit.Write(
                action: "ListingPromotion.Expired",
                entityType: "ListingPromotion",
                entityId: promo.ListingPromotionId.ToString("N"),
                actorUserId: null,
                before: new { Status = ListingPromotionStatus.Active.ToString() },
                after: new { Status = ListingPromotionStatus.Expired.ToString(), promo.ProductId, promo.EndsAtUtc });
        }

        if (lapsed.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("ListingPromotionExpiry: expired {Count} promotion(s).", lapsed.Count);
        }

        return lapsed.Count;
    }
}
