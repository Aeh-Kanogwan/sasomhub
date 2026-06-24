using Marketplace.Application.Notifications;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// B-01/G-1, FR-27/FR-32: delivers Pending notifications (lifecycle advance-notices created by
/// <see cref="MembershipTrialExpiryService"/>, plus auction-close notices created at auction close).
/// Email/SMS are delivered through the real <see cref="INotificationSender"/> (M1) which calls the
/// configured provider and persists a NotificationDeliveryLog row (LEGAL #8); in-app is delivered by
/// simply marking the row Sent so the member's feed can read it. On delivery we set Status=Sent + SentAtUtc.
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
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();

        var now = DateTime.UtcNow;

        // Due, not-yet-sent notices (uses IX_Notif_Due: filtered on Status='Pending').
        // Include the recipient User so Email/SMS sends have an address/phone to deliver to.
        var due = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.ScheduledForUtc <= now)
            .OrderBy(n => n.ScheduledForUtc)
            .Take(BatchSize)
            .Include(n => n.User)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var notice in due)
        {
            var ok = await DeliverAsync(sender, notice, ct);
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
    /// Channel delivery. Email/SMS go through the real provider via <see cref="INotificationSender"/>,
    /// which persists a NotificationDeliveryLog row (LEGAL #8) on every attempt; in-app is a no-op
    /// success (the row itself IS the in-app message once Status=Sent). Returns true on success.
    /// </summary>
    private async Task<bool> DeliverAsync(INotificationSender sender, Notification notice, CancellationToken ct)
    {
        switch (notice.Channel)
        {
            case NotificationChannel.Email:
            case NotificationChannel.Sms:
            {
                var channel = notice.Channel == NotificationChannel.Email ? SendChannel.Email : SendChannel.Sms;
                var recipient = notice.Channel == NotificationChannel.Email
                    ? notice.User?.Email
                    : notice.User?.PhoneNumber;

                if (string.IsNullOrWhiteSpace(recipient))
                {
                    _logger.LogWarning("Notice {Id} ({Channel}) has no recipient address; marking failed.",
                        notice.NotificationId, notice.Channel);
                    return false;
                }

                // TemplateKey/Version derived from the notice type; variables drive the rendered text.
                // The sender masks the recipient + writes the delivery-log evidence row.
                var request = new SendMessageRequest(
                    Channel: channel,
                    Recipient: recipient,
                    TemplateKey: $"notification.{notice.Type}",
                    TemplateVersion: "1",
                    Variables: new Dictionary<string, string>
                    {
                        ["type"] = notice.Type.ToString(),
                        ["milestone"] = notice.Milestone?.ToString() ?? string.Empty,
                        ["membershipId"] = notice.RelatedMembershipId?.ToString() ?? string.Empty,
                    },
                    UserId: notice.UserId,
                    NotificationId: notice.NotificationId,
                    CorrelationId: notice.NotificationId);

                var result = await sender.SendAsync(request, ct);
                if (!result.Succeeded)
                    _logger.LogWarning("Notice {Id} ({Channel}) provider send failed: {Error}",
                        notice.NotificationId, notice.Channel, result.Error);
                return result.Succeeded;
            }

            case NotificationChannel.InApp:
            default:
                // In-app feed reads Sent notices directly; nothing external to call.
                return true;
        }
    }
}
