using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// FR-27: watches 3-month free trials and annual paid memberships.
///  - Sends advance reminders at 14 / 3 / 1 days before trial end or renewal (email/in-app) + logs them.
///  - When TrialEndsAtUtc / PaidThroughUtc passes without payment => Status -> Expired (bid/listing suspended).
///  - Never charges silently: auto-renew only after the advance reminder was sent and a payment method consented.
/// Skeleton — sweep loop only.
/// </summary>
public class MembershipTrialExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly int[] ReminderDays = { 14, 3, 1 }; // FR-27 advance-notice schedule
    private readonly IServiceProvider _services;
    private readonly ILogger<MembershipTrialExpiryService> _logger;

    public MembershipTrialExpiryService(IServiceProvider services, ILogger<MembershipTrialExpiryService> logger)
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

                var now = DateTime.UtcNow;

                // Memberships whose trial OR paid term has lapsed (uses UX_Membership_LivePerUser).
                var expiredCount = await db.Memberships
                    .Where(m =>
                        (m.Status == Domain.Enums.MembershipStatus.Trial && m.TrialEndsAtUtc != null && m.TrialEndsAtUtc <= now) ||
                        (m.Status == Domain.Enums.MembershipStatus.Active && m.PaidThroughUtc != null && m.PaidThroughUtc <= now))
                    .CountAsync(stoppingToken);

                // TODO: send 14/3/1-day reminders (compute against TrialEndsAtUtc/PaidThroughUtc),
                //       flip lapsed memberships to Expired, run consented auto-renew + FeeInvoice,
                //       write AuditLog for every status change/notification.
                if (expiredCount > 0)
                    _logger.LogInformation("MembershipTrialExpiry: {Count} memberships lapsed (skeleton, not processed).", expiredCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MembershipTrialExpiry sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
