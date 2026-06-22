using Marketplace.Application.Auditing;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Worker.Services;

/// <summary>
/// FR-27 / FR-32: watches 3-month free trials and annual paid memberships.
///  - Creates advance-notice Notification rows at 14 / 3 / 1 days before trial end / paid-through.
///    Dedupe is enforced by the filtered unique index UX_Notif_NoDup (UserId, Type, MembershipId,
///    Milestone) — the worker can re-run safely (idempotent). Delivery is done by
///    <see cref="NotificationDispatchService"/>.
///  - When TrialEndsAtUtc / PaidThroughUtc passes => flips Status -> Expired (bid/listing suspended,
///    FR-06/FR-10), creates a MembershipExpired notice, and writes an append-only AuditLog (FR-24).
///
/// AUTO-RENEW (FR-27): this worker deliberately does NOT auto-charge. Flow B requires an Admin to
/// confirm a bank-transfer slip before a membership is extended, and there is no stored payment
/// mandate to charge against. So on a lapsing paid term we only create a RenewalDue notice (the legal
/// precondition: prove the member was warned) and let the EXPIRED flip happen if they do not pay.
/// No silent charge — consistent with consumer-protection law and the no-touch revenue model.
/// </summary>
public class MembershipTrialExpiryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly int[] ReminderDays = { 14, 3, 1 }; // FR-27 advance-notice schedule (must match UX_Notif_NoDup milestones)
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
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MembershipTrialExpiry sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>One sweep: create due milestone notices, then flip lapsed memberships to Expired. Idempotent.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var now = DateTime.UtcNow;
        var created = await CreateDueMilestoneNoticesAsync(db, now, ct);
        var expired = await ExpireLapsedMembershipsAsync(db, audit, now, ct);

        if (created > 0 || expired > 0)
            _logger.LogInformation("MembershipTrialExpiry: created {Notices} advance-notice(s), expired {Expired} membership(s).",
                created, expired);
    }

    /// <summary>
    /// FR-32: for each live membership inside the 14-day notice horizon, create the Notification rows for
    /// every milestone whose threshold has been crossed and not yet logged. UX_Notif_NoDup dedupes; on a
    /// concurrent/dup insert we swallow the unique violation (idempotent).
    /// </summary>
    private async Task<int> CreateDueMilestoneNoticesAsync(MarketplaceDbContext db, DateTime now, CancellationToken ct)
    {
        var horizon = now.AddDays(ReminderDays.Max());

        // Live memberships whose trial/paid term expires within the notice horizon (uses IX_Membership_Expiry).
        var expiring = await db.Memberships
            .Where(m =>
                (m.Status == MembershipStatus.Trial && m.TrialEndsAtUtc != null &&
                 m.TrialEndsAtUtc > now && m.TrialEndsAtUtc <= horizon) ||
                (m.Status == MembershipStatus.Active && m.PaidThroughUtc != null &&
                 m.PaidThroughUtc > now && m.PaidThroughUtc <= horizon))
            .Select(m => new { m.MembershipId, m.UserId, m.Status, m.TrialEndsAtUtc, m.PaidThroughUtc })
            .ToListAsync(ct);

        var created = 0;
        foreach (var m in expiring)
        {
            var expiresAt = m.Status == MembershipStatus.Trial ? m.TrialEndsAtUtc!.Value : m.PaidThroughUtc!.Value;
            var type = m.Status == MembershipStatus.Trial ? NotificationType.TrialExpiring : NotificationType.RenewalDue;
            var daysLeft = (expiresAt - now).TotalDays;

            foreach (var milestone in ReminderDays)
            {
                // The milestone is "crossed" once we are within that many days of expiry.
                if (daysLeft > milestone) continue;

                // App-level dedupe pre-check (the DB index is the hard guarantee).
                var already = await db.Notifications.AnyAsync(n =>
                    n.UserId == m.UserId && n.Type == type &&
                    n.RelatedMembershipId == m.MembershipId && n.Milestone == milestone, ct);
                if (already) continue;

                db.Notifications.Add(new Notification
                {
                    UserId = m.UserId,
                    Type = type,
                    Channel = NotificationChannel.Email, // lifecycle reminders go by email (delivery stubbed)
                    RelatedMembershipId = m.MembershipId,
                    Milestone = milestone,
                    ScheduledForUtc = now,
                    Status = NotificationStatus.Pending,
                    CreatedAtUtc = now,
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                    created++;
                }
                catch (DbUpdateException ex) when (IsDuplicateKey(ex))
                {
                    // Lost the race on UX_Notif_NoDup — another sweep already logged this milestone. Idempotent.
                    db.ChangeTracker.Clear();
                }
            }
        }

        return created;
    }

    /// <summary>
    /// FR-27: flip memberships whose trial/paid term has elapsed to Expired (suspends bid/listing). Creates a
    /// MembershipExpired notice and an append-only AuditLog. Idempotent: only live rows are selected, so a
    /// re-run after the flip is a no-op.
    /// </summary>
    private async Task<int> ExpireLapsedMembershipsAsync(MarketplaceDbContext db, IAuditService audit, DateTime now, CancellationToken ct)
    {
        var lapsed = await db.Memberships
            .Where(m =>
                (m.Status == MembershipStatus.Trial && m.TrialEndsAtUtc != null && m.TrialEndsAtUtc <= now) ||
                (m.Status == MembershipStatus.Active && m.PaidThroughUtc != null && m.PaidThroughUtc <= now))
            .ToListAsync(ct);

        foreach (var m in lapsed)
        {
            var fromStatus = m.Status;
            m.Status = MembershipStatus.Expired;
            m.EndAtUtc = now;
            m.UpdatedAtUtc = now;

            // FR-32: one-off "membership expired" notice (milestone-less => outside the lifecycle dedupe index;
            // guarded by an app-level pre-check so a re-run before delivery does not pile up duplicates).
            var noticeExists = await db.Notifications.AnyAsync(n =>
                n.UserId == m.UserId && n.Type == NotificationType.MembershipExpired &&
                n.RelatedMembershipId == m.MembershipId, ct);
            if (!noticeExists)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = m.UserId,
                    Type = NotificationType.MembershipExpired,
                    Channel = NotificationChannel.Email,
                    RelatedMembershipId = m.MembershipId,
                    Milestone = null,
                    ScheduledForUtc = now,
                    Status = NotificationStatus.Pending,
                    CreatedAtUtc = now,
                });
            }

            // FR-24: append-only audit of the status transition (system actor).
            audit.Write(
                action: "Membership.Expired",
                entityType: "Membership",
                entityId: m.MembershipId.ToString("N"),
                actorUserId: null,
                before: new { Status = fromStatus.ToString() },
                after: new { Status = MembershipStatus.Expired.ToString(), m.TrialEndsAtUtc, m.PaidThroughUtc });
        }

        if (lapsed.Count > 0)
            await db.SaveChangesAsync(ct);

        return lapsed.Count;
    }

    /// <summary>SQL Server unique-index violation (2601/2627) — maps a notice dedupe race to idempotent success.</summary>
    private static bool IsDuplicateKey(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.SqlClient.SqlException sql
           && (sql.Number == 2601 || sql.Number == 2627);
}
