using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Entities;

// ============================ 9. TRUST SCORE ==============================

/// <summary>dbo.TrustScores — 1:1 with User, CASCADE. Default 100, CHECK 0-100 (FR-13/14).</summary>
public class TrustScore
{
    public Guid UserId { get; set; }
    public int Score { get; set; } = 100;
    public DateTime LastCalculatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public User User { get; set; } = null!;
}

/// <summary>dbo.TrustScoreHistory — append log of every score change with standard reason code.</summary>
public class TrustScoreHistory
{
    public long TrustScoreHistoryId { get; set; }
    public Guid UserId { get; set; }
    public int Delta { get; set; }
    public int ScoreAfter { get; set; }
    public int? ReasonCodeId { get; set; }
    public Guid? RelatedTransactionId { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }     // null => system
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public BlacklistReasonCode? ReasonCode { get; set; }
    public Transaction? RelatedTransaction { get; set; }
}

// ============================ 10. PENALTY / BLACKLIST =====================

/// <summary>dbo.PenaltyActions — automated/manual punishment with due process (FR-15).</summary>
public class PenaltyAction
{
    public Guid PenaltyActionId { get; set; }
    public Guid UserId { get; set; }
    public PenaltyActionType ActionType { get; set; }
    public int ReasonCodeId { get; set; }
    public Guid? RelatedTransactionId { get; set; }
    public Guid? IssuedByUserId { get; set; }      // null => auto
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public BlacklistReasonCode ReasonCode { get; set; } = null!;
    public Transaction? RelatedTransaction { get; set; }
    public User? IssuedByUser { get; set; }
}

/// <summary>
/// dbo.BlacklistEntries — internal warn-on-deal register with due process (FR-17, Legal #3).
/// Uses standard reason codes (no defamatory free text); supports ReviewStatus/AppealStatus/ExpiresAt.
/// </summary>
public class BlacklistEntry
{
    public Guid BlacklistEntryId { get; set; }
    public Guid UserId { get; set; }
    public int ReasonCodeId { get; set; }
    public string? InternalNote { get; set; }
    public BlacklistReviewStatus ReviewStatus { get; set; } = BlacklistReviewStatus.PendingReview;
    public BlacklistAppealStatus AppealStatus { get; set; } = BlacklistAppealStatus.None;
    public string? AppealNote { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public BlacklistReasonCode ReasonCode { get; set; } = null!;
    public User? CreatedByUser { get; set; }
    public User? ReviewedByUser { get; set; }
}
