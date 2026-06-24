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

// ============================ 21. NOTIFICATION DELIVERY LOG (M1) ==========

/// <summary>
/// dbo.NotificationDeliveryLog — APPEND-ONLY evidence of every real outbound Email/SMS
/// send attempt through a concrete provider (M1). LEGAL #8 / FR-27 / FR-32: we must be able
/// to PROVE that a given notice was actually handed to a provider, to whom, when, and with
/// what outcome — independent of the lifecycle <see cref="Notification"/> row.
///
/// Designed as legal evidence:
///  - <see cref="RecipientMasked"/> stores a masked address (e.g. j***@x.com / 08x-xxx-1234), never the raw PII.
///  - <see cref="ProviderMessageId"/> is the provider's receipt id for dispute/audit reconciliation.
///  - <see cref="PayloadSnapshotJson"/> snapshots the rendered template variables (NOT secrets) at send time.
///  - <see cref="RetentionExpiresAtUtc"/> bounds PDPA retention (purged by a worker after expiry).
/// Treated as append-only (DB trigger TR_NotifDelivery_NoModify); status changes append a NEW row
/// referencing the same <see cref="CorrelationId"/> rather than UPDATE-ing an existing row.
/// </summary>
public class NotificationDeliveryLog
{
    public long NotificationDeliveryLogId { get; set; }

    /// <summary>Optional link to the lifecycle notice this send fulfils (NULL for ad-hoc/transactional sends).</summary>
    public Guid? NotificationId { get; set; }

    /// <summary>Optional recipient user (NULL for sends to non-users, e.g. unverified email).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Provider key e.g. 'SendGrid','SES','TwilioSms','SmtpDev','SmsMock' — which abstraction handled it.</summary>
    public string Provider { get; set; } = null!;

    /// <summary>Provider's message receipt id (for delivery reconciliation / webhooks). NULL until accepted.</summary>
    public string? ProviderMessageId { get; set; }

    public DeliveryChannel Channel { get; set; }

    /// <summary>Masked recipient address — never the raw email/phone (PDPA data-minimisation).</summary>
    public string RecipientMasked { get; set; } = null!;

    /// <summary>
    /// Keyed HMAC-SHA256 fingerprint (hex) of the normalised recipient — lets us match the same
    /// recipient across rows without storing PII (the pepper makes it non-reversible). NULL only for
    /// legacy rows / when no pepper is configured. (M1: moved out of PayloadSnapshotJson into a column.)
    /// </summary>
    public string? RecipientHash { get; set; }

    /// <summary>Template identifier e.g. 'membership.renewal-due' so we know which message was sent.</summary>
    public string TemplateKey { get; set; } = null!;

    /// <summary>Version of the template text shown — proves which wording applied (cf. DisclaimerVersion).</summary>
    public string TemplateVersion { get; set; } = null!;

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Queued;

    /// <summary>Provider/transport error detail when Status=Failed/Retrying (truncated, no PII).</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>Retry attempt counter (0 = first try) for FR-32 retry policy.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Snapshot of rendered template variables at send time (JSON, no secrets/no raw PII).</summary>
    public string? PayloadSnapshotJson { get; set; }

    /// <summary>Correlates all attempts of one logical notification (ties retries together, cf. AuditLog.CorrelationId).</summary>
    public Guid? CorrelationId { get; set; }

    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>PDPA retention boundary; a purge worker deletes rows past this instant.</summary>
    public DateTime RetentionExpiresAtUtc { get; set; }

    public Notification? Notification { get; set; }
    public User? User { get; set; }
}

// ============================ 22. PDPA DSAR (M3) ==========================

/// <summary>
/// dbo.DataSubjectRequests — PDPA data-subject access/erasure requests (M3, FR-01/04).
/// Tracks the lifecycle of an Export / Erasure / Access / Rectify / WithdrawConsent request
/// with a statutory <see cref="DueByUtc"/> deadline. The actual export artifact path is stored
/// (not the data itself); erasure is performed via the existing soft-delete/anonymize on User.
/// Append-friendly: status transitions are mirrored to AuditLogs; the row itself is mutable only
/// through the service (no DB no-modify trigger) so handlers can advance status &amp; attach results.
/// </summary>
public class DataSubjectRequest
{
    public Guid DataSubjectRequestId { get; set; }

    public DataSubjectRequestType RequestType { get; set; }
    public DataSubjectRequestStatus Status { get; set; } = DataSubjectRequestStatus.Pending;

    /// <summary>The data subject who made the request.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>When the requester's identity was verified (PDPA: must verify before fulfilling).</summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>Admin/Support who handled the request (NULL while unassigned).</summary>
    public Guid? HandledByUserId { get; set; }

    /// <summary>Path/URI to the generated export package (Export/Access only); never the data inline.</summary>
    public string? ResultArtifactPath { get; set; }

    /// <summary>Statutory response deadline (e.g. requested + 30 days) for SLA tracking.</summary>
    public DateTime DueByUtc { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public User RequestedByUser { get; set; } = null!;
    public User? HandledByUser { get; set; }
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
