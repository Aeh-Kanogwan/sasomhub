using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// B-01/G-1, FR-27/FR-32: delivers Pending notifications (lifecycle advance-notices created by
/// <see cref="MembershipTrialExpiryService"/>, plus auction-close notices created at auction close).
/// Email is a STUB here (logged, not SMTP); in-app is delivered by simply marking the row Sent so the
/// member's notification feed can read it. On delivery we set Status=Sent + SentAtUtc.
///
/// Idempotency: we only pick rows whose Status = Pending and re-stamp them to Sent/Failed, so a re-run
/// never re-delivers an already-Sent notice. Failures set Status=Failed (kept eligible for a future
/// retry only if a separate policy re-opens them — we do NOT silently treat a failure as Sent, FR-32).
/// </summary>
public class NotificationDispatchService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 200;
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
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotificationDispatch sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>One dispatch sweep: deliver due Pending notices and stamp Sent/Failed. Idempotent. Returns count delivered.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();

        var now = DateTime.UtcNow;

        // Due, not-yet-sent notices (uses IX_Notif_Due: filtered on Status='Pending').
        var due = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.ScheduledForUtc <= now)
            .OrderBy(n => n.ScheduledForUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var notice in due)
        {
            var ok = await DeliverAsync(notice, ct);
            notice.Status = ok ? NotificationStatus.Sent : NotificationStatus.Failed;
            if (ok)
            {
                notice.SentAtUtc = DateTime.UtcNow;
                sent++;
            }
        }

        if (due.Count > 0)
            await db.SaveChangesAsync(ct);

        if (due.Count > 0)
            _logger.LogInformation("NotificationDispatch: delivered {Sent}/{Total} pending notices.", sent, due.Count);

        return sent;
    }

    /// <summary>
    /// Channel delivery. Email/SMS are stubbed (logged only — no SMTP/SMS provider wired in MVP); in-app is
    /// a no-op success (the row itself IS the in-app message once Status=Sent). Returns true on success.
    /// </summary>
    private Task<bool> DeliverAsync(Notification notice, CancellationToken ct)
    {
        switch (notice.Channel)
        {
            case NotificationChannel.Email:
                _logger.LogInformation(
                    "[EMAIL stub] notice {Id} type={Type} milestone={Milestone} -> user {User} (membership {Membership}).",
                    notice.NotificationId, notice.Type, notice.Milestone, notice.UserId, notice.RelatedMembershipId);
                return Task.FromResult(true);

            case NotificationChannel.Sms:
                _logger.LogInformation("[SMS stub] notice {Id} type={Type} -> user {User}.",
                    notice.NotificationId, notice.Type, notice.UserId);
                return Task.FromResult(true);

            case NotificationChannel.InApp:
            default:
                // In-app feed reads Sent notices directly; nothing external to call.
                return Task.FromResult(true);
        }
    }
}
