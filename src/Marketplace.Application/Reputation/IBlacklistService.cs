using Marketplace.Application.Common;

namespace Marketplace.Application.Reputation;

// M5: internal warn-on-deal blacklist (FR-17, Legal #3). Two faces:
//   (a) warn-on-deal QUERY — when a member views/deals with a counterparty, surface an active,
//       CONFIRMED warning ("this user has unresolved issues") WITHOUT exposing defamatory detail.
//   (b) admin ban/unban — propose/confirm/reject entries with due process (no auto-permaban, DP-3):
//       severe cases set User.AccountStatus = PendingBanReview, an Admin confirms -> Banned, or unbans.
// Owns dbo.BlacklistEntries (ReviewStatus/AppealStatus) + drives User.AccountStatus on confirm.
// Standard reason codes only (Legal #3 — never free-text defamation). All actions audited.

/// <summary>Warn-on-deal banner shown on a listing/counterparty (FR-17) — code/severity only, no free text.</summary>
public record BlacklistWarning(Guid UserId, int ReasonCodeId, string ReasonDisplayName, byte Severity, DateTime EffectiveAtUtc);

/// <summary>Admin proposes a blacklist entry against a user (status PendingReview).</summary>
public record ProposeBlacklistRequest(Guid UserId, int ReasonCodeId, string? InternalNote, Guid ProposedByUserId);

/// <summary>Admin confirms (-> ban) or rejects a proposed blacklist entry (due process DP-3).</summary>
public record ReviewBlacklistRequest(Guid BlacklistEntryId, Guid ReviewedByUserId, bool Confirm, string? Note);

public record BlacklistEntryDto(
    Guid BlacklistEntryId,
    Guid UserId,
    int ReasonCodeId,
    string ReviewStatus,
    string AppealStatus,
    bool IsActive,
    DateTime EffectiveAtUtc,
    DateTime? ExpiresAtUtc);

/// <summary>M5 internal blacklist + warn-on-deal service (FR-17). Due process; audited.</summary>
public interface IBlacklistService
{
    /// <summary>
    /// FR-17 warn-on-deal: active CONFIRMED warnings for a user, for the listing/counterparty banner.
    /// Returns code + severity only (Legal #3 — no defamatory detail leaks to other members).
    /// </summary>
    Task<Result<IReadOnlyList<BlacklistWarning>>> GetActiveWarningsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Admin: list blacklist entries by review status (NULL = all).</summary>
    Task<Result<IReadOnlyList<BlacklistEntryDto>>> GetForAdminAsync(string? reviewStatus, CancellationToken ct = default);

    /// <summary>FR-17: admin proposes an entry (status PendingReview) — does NOT ban yet (DP-3).</summary>
    Task<Result<BlacklistEntryDto>> ProposeAsync(ProposeBlacklistRequest request, CancellationToken ct = default);

    /// <summary>
    /// FR-17 due process: admin confirms (Confirmed + User.AccountStatus=Banned) or rejects an entry.
    /// Human-in-the-loop only — there is never an automatic permanent ban.
    /// </summary>
    Task<Result<BlacklistEntryDto>> ReviewAsync(ReviewBlacklistRequest request, CancellationToken ct = default);

    /// <summary>Admin: lift an active confirmed entry (IsActive=false) and restore account status.</summary>
    Task<Result<BlacklistEntryDto>> UnbanAsync(Guid blacklistEntryId, Guid reviewedByUserId, string? note, CancellationToken ct = default);
}
