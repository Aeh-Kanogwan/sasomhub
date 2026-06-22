using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// B-01/G-1, FR-27: dispatches membership advance-notice reminders at 14 / 3 / 1 days before
/// trial end or annual renewal, and writes a Notification log row for every notice (LEGAL #8:
/// must be able to PROVE a notice was sent before any charge). Auto-renew is only permitted
/// AFTER the advance notice was logged.
///
/// Idempotency: the filtered unique index UX_Notif_NoDup (UserId, Type, RelatedMembershipId,
/// Milestone) guarantees a milestone notice is created at most once per membership, so this loop
/// can re-run safely. The sweep uses IX_Membership_Expiry (S-03) to find expiring memberships fast.
/// Skeleton — sweep loop only.
/// </summary>
public class NotificationDispatchService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly int[] Milestones = { 14, 3, 1 }; // FR-27 advance-notice schedule (days before expiry)
    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(IServiceProvider services, ILogger<NotificationDispatchService> logger)
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
                var horizon = now.AddDays(Milestones.Max()); // furthest milestone we look ahead to

                // Live memberships whose trial/paid term expires within the notice horizon (IX_Membership_Expiry).
                var expiring = await db.Memberships
                    .Where(m =>
                        (m.Status == MembershipStatus.Trial && m.TrialEndsAtUtc != null &&
                         m.TrialEndsAtUtc > now && m.TrialEndsAtUtc <= horizon) ||
                        (m.Status == MembershipStatus.Active && m.PaidThroughUtc != null &&
                         m.PaidThroughUtc > now && m.PaidThroughUtc <= horizon))
                    .Select(m => new { m.MembershipId, m.UserId, m.Status, m.TrialEndsAtUtc, m.PaidThroughUtc })
                    .ToListAsync(stoppingToken);

                var pending = await db.Notifications
                    .CountAsync(n => n.Status == NotificationStatus.Pending && n.ScheduledForUtc <= now, stoppingToken);

                // TODO (FR-27 / B-01): for each expiring membership and each milestone whose threshold
                //   has been crossed, insert a Notification row (Type=TrialExpiring/RenewalDue,
                //   Milestone=14/3/1, RelatedMembershipId=...) relying on UX_Notif_NoDup to dedupe;
                //   then deliver Pending notices via the configured Channel (Email/InApp/Sms), set
                //   Status=Sent + SentAtUtc on success / Failed otherwise, and write an AuditLog row.
                //   Only after the notice is logged may MembershipTrialExpiryService run consented auto-renew.
                if (expiring.Count > 0 || pending > 0)
                    _logger.LogInformation(
                        "NotificationDispatch: {Expiring} memberships in notice horizon, {Pending} pending notices (skeleton, not processed).",
                        expiring.Count, pending);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotificationDispatch sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
