using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Entities;

// ============================ 16. PDPA : CONSENT & AUDIT ==================

/// <summary>dbo.ConsentRecords — append-only PDPA consent log (grant/withdraw). IP stored as hash only (FR-01/04).</summary>
public class ConsentRecord
{
    public long ConsentRecordId { get; set; }
    public Guid UserId { get; set; }
    public string ConsentType { get; set; } = null!;   // ToS/Privacy/Marketing/DataProcessing
    public string DocumentVersion { get; set; } = null!;
    public bool IsGranted { get; set; }
    public byte[]? SourceIpHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
}

/// <summary>
/// dbo.AuditLogs — append-only audit trail (FR-24, NFR-A1). who/what/when/before-after.
/// Membership changes, credit grants/spends and referral rewards MUST be written here by the app layer.
/// </summary>
public class AuditLog
{
    public long AuditLogId { get; set; }
    public Guid? ActorUserId { get; set; }         // null => system
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public byte[]? IpAddressHash { get; set; }
    /// <summary>
    /// S-06: per-transaction correlation id (NFR-A2) — ties together all audit rows produced
    /// within one logical business operation / HTTP request for AML &amp; due-process tracing.
    /// </summary>
    public Guid? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User? ActorUser { get; set; }
}

// ============================ 17. NOTIFICATIONS (B-01 / G-1) ==============

/// <summary>
/// dbo.Notifications — advance-notice log (FR-27): 14/3/1-day reminders before trial/renewal
/// expiry plus a LOG of every notice sent (LEGAL #8: must prove a notice was sent before any charge).
/// Doubles as the notification worker's idempotency guard — filtered unique index
/// UX_Notif_NoDup enforces at most one lifecycle notice per (user, type, membership, milestone).
/// </summary>
public class Notification
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public Guid? RelatedMembershipId { get; set; }
    public int? Milestone { get; set; }            // days-before-expiry bucket: 14/3/1; NULL for non-lifecycle
    public DateTime ScheduledForUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public Membership? RelatedMembership { get; set; }
}

// ============================ 19. CONFIG VERSIONING (B-04 / G-4) ==========

/// <summary>
/// dbo.ConfigVersions — versioned, immutable history of admin-editable config (FR-31):
/// prices, credit costs, KYC threshold, packages. Resolve "value of key K at time T" =
/// latest EffectiveFromUtc &lt;= T. Never applied retroactively to already-paid cycles
/// (the charged price is also snapshotted on Membership.PaidAmountTHB, Y-06).
/// </summary>
public class ConfigVersion
{
    public long ConfigVersionId { get; set; }
    public string ConfigKey { get; set; } = null!;   // e.g. 'MembershipTier.Premium.AnnualPriceTHB'
    public string Value { get; set; } = null!;        // string-encoded (number/json), interpreted by app
    public DateTime EffectiveFromUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }        // admin who changed it (null = system/seed)
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User? CreatedByUser { get; set; }
}
