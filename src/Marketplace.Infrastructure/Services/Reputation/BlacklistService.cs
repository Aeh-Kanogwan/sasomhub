using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Reputation;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Reputation;

/// <summary>
/// M5 — FR-17 internal "warn-on-deal" blacklist with DUE PROCESS (Legal #3, DP-3). Two faces:
///   (a) warn-on-deal QUERY (<see cref="GetActiveWarningsAsync"/>): when a member views a
///       counterparty/listing, surface active CONFIRMED warnings as REASON CODE + SEVERITY ONLY.
///       The InternalNote / free text NEVER leaves this service to other members (no public defamation).
///   (b) admin ban/unban: propose (PendingReview, no ban) -> human Admin confirms (-> Banned) or
///       rejects (-> Rejected, account restored), and unban lifts an active confirmed entry.
///
/// HUMAN-IN-THE-LOOP INVARIANT (DP-3): there is NEVER an automatic permanent ban. A "severe" reason
/// only PROPOSES an entry and flags the account <see cref="AccountStatus.PendingBanReview"/>; an Admin
/// must confirm before <see cref="AccountStatus.Banned"/> is set.
///
/// Every action writes an append-only dbo.AuditLogs row (FR-24) in the SAME SaveChanges as the change.
/// </summary>
public sealed class BlacklistService : IBlacklistService
{
    /// <summary>
    /// Reason severity at/above which a proposal auto-flags the account PendingBanReview (still
    /// awaiting human confirmation — never an auto-ban). 4 = Counterfeit item, 5 = Payment fraud.
    /// </summary>
    private const byte SevereThreshold = 4;

    private readonly MarketplaceDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<BlacklistService> _logger;

    public BlacklistService(MarketplaceDbContext db, IAuditService audit, ILogger<BlacklistService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// FR-17 warn-on-deal: active CONFIRMED, non-expired warnings for a user. Projects to
    /// <see cref="BlacklistWarning"/> = reason code id + display name + severity only (Legal #3 —
    /// no InternalNote / free text leaks to the viewing counterparty).
    /// </summary>
    public async Task<Result<IReadOnlyList<BlacklistWarning>>> GetActiveWarningsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Only CONFIRMED + IsActive + not-expired entries are warnings. Select ONLY code/severity —
        // the projection deliberately omits InternalNote so defamatory detail can never surface here.
        var warnings = await _db.BlacklistEntries.AsNoTracking()
            .Where(e => e.UserId == userId
                        && e.IsActive
                        && e.ReviewStatus == BlacklistReviewStatus.Confirmed
                        && (e.ExpiresAtUtc == null || e.ExpiresAtUtc > now))
            .OrderByDescending(e => e.ReasonCode.Severity)
            .ThenByDescending(e => e.EffectiveAtUtc)
            .Select(e => new BlacklistWarning(
                e.UserId,
                e.ReasonCodeId,
                e.ReasonCode.DisplayName,
                e.ReasonCode.Severity,
                e.EffectiveAtUtc))
            .ToListAsync(ct);

        return Result<IReadOnlyList<BlacklistWarning>>.Success(warnings);
    }

    /// <summary>Admin: list entries by review status (NULL/empty = all), newest-first.</summary>
    public async Task<Result<IReadOnlyList<BlacklistEntryDto>>> GetForAdminAsync(string? reviewStatus, CancellationToken ct = default)
    {
        var query = _db.BlacklistEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(reviewStatus))
        {
            if (!Enum.TryParse<BlacklistReviewStatus>(reviewStatus, ignoreCase: true, out var parsed))
                return Result<IReadOnlyList<BlacklistEntryDto>>.Fail($"Unknown blacklist review status '{reviewStatus}'.");
            query = query.Where(e => e.ReviewStatus == parsed);
        }

        var entries = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);

        IReadOnlyList<BlacklistEntryDto> dtos = entries.Select(ToDto).ToList();
        return Result<IReadOnlyList<BlacklistEntryDto>>.Success(dtos);
    }

    /// <summary>
    /// FR-17/DP-3: admin proposes an entry against a user. Creates a PendingReview entry — it does
    /// NOT ban. For a severe reason (severity >= <see cref="SevereThreshold"/>) the account is flagged
    /// <see cref="AccountStatus.PendingBanReview"/> so it shows in the human review queue; a non-severe
    /// proposal leaves the account status untouched until confirmed.
    /// </summary>
    public async Task<Result<BlacklistEntryDto>> ProposeAsync(ProposeBlacklistRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == request.UserId, ct);
        if (user is null)
            return Result<BlacklistEntryDto>.Fail("User not found.");

        // Standard reason code only (Legal #3 — code, not free-text accusation, is the classifier).
        var reason = await _db.BlacklistReasonCodes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReasonCodeId == request.ReasonCodeId && r.IsActive, ct);
        if (reason is null)
            return Result<BlacklistEntryDto>.Fail("Invalid or inactive reason code.");

        // One open proposal per user/reason — avoid duplicate pending reviews stacking up.
        var alreadyPending = await _db.BlacklistEntries.AsNoTracking().AnyAsync(
            e => e.UserId == request.UserId
                 && e.ReasonCodeId == request.ReasonCodeId
                 && e.ReviewStatus == BlacklistReviewStatus.PendingReview,
            ct);
        if (alreadyPending)
            return Result<BlacklistEntryDto>.Fail("A pending blacklist proposal already exists for this user and reason.");

        var now = DateTime.UtcNow;
        var entry = new BlacklistEntry
        {
            BlacklistEntryId = Guid.NewGuid(),
            UserId = request.UserId,
            ReasonCodeId = request.ReasonCodeId,
            InternalNote = string.IsNullOrWhiteSpace(request.InternalNote) ? null : request.InternalNote.Trim(),
            ReviewStatus = BlacklistReviewStatus.PendingReview,
            AppealStatus = BlacklistAppealStatus.None,
            CreatedByUserId = request.ProposedByUserId,
            EffectiveAtUtc = now,
            // Not active and not a warning until an Admin CONFIRMS it (warn-on-deal reads Confirmed only).
            IsActive = false,
            CreatedAtUtc = now,
        };
        _db.BlacklistEntries.Add(entry);

        // DP-3: severe reason flags the account for HUMAN review (PendingBanReview), never an auto-ban.
        var flaggedForReview = false;
        var previousStatus = user.AccountStatus;
        if (reason.Severity >= SevereThreshold && user.AccountStatus == AccountStatus.Active)
        {
            user.AccountStatus = AccountStatus.PendingBanReview;
            flaggedForReview = true;
        }

        _audit.Write(
            action: "Blacklist.Proposed",
            entityType: "BlacklistEntry",
            entityId: entry.BlacklistEntryId.ToString("N"),
            actorUserId: request.ProposedByUserId,
            after: new
            {
                entry.BlacklistEntryId,
                entry.UserId,
                entry.ReasonCodeId,
                ReviewStatus = entry.ReviewStatus.ToString(),
                Severity = reason.Severity,
                FlaggedForReview = flaggedForReview,
                AccountStatusBefore = previousStatus.ToString(),
                AccountStatusAfter = user.AccountStatus.ToString(),
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Blacklist proposed {EntryId} for user {UserId} (reason {ReasonCodeId}, severity {Severity}, flagged={Flagged}) by {Actor}.",
            entry.BlacklistEntryId, request.UserId, request.ReasonCodeId, reason.Severity, flaggedForReview, request.ProposedByUserId);

        return Result<BlacklistEntryDto>.Success(ToDto(entry));
    }

    /// <summary>
    /// FR-17 due process: admin CONFIRMS (-> Confirmed + IsActive + User.AccountStatus=Banned) or
    /// REJECTS (-> Rejected; account restored to Active if it was only PendingBanReview). Human action
    /// only — this is the single place a ban is ever applied.
    /// </summary>
    public async Task<Result<BlacklistEntryDto>> ReviewAsync(ReviewBlacklistRequest request, CancellationToken ct = default)
    {
        var entry = await _db.BlacklistEntries
            .FirstOrDefaultAsync(e => e.BlacklistEntryId == request.BlacklistEntryId, ct);
        if (entry is null)
            return Result<BlacklistEntryDto>.Fail("Blacklist entry not found.");

        if (entry.ReviewStatus != BlacklistReviewStatus.PendingReview)
            return Result<BlacklistEntryDto>.Fail($"Entry is not pending review (current status: {entry.ReviewStatus}).");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == entry.UserId, ct);
        if (user is null)
            return Result<BlacklistEntryDto>.Fail("User not found.");

        var now = DateTime.UtcNow;
        var fromReview = entry.ReviewStatus;
        var fromAccount = user.AccountStatus;

        if (request.Confirm)
        {
            // Confirm -> active warning + ban the account (the only place a ban is applied, DP-3).
            entry.ReviewStatus = BlacklistReviewStatus.Confirmed;
            entry.IsActive = true;
            entry.EffectiveAtUtc = now;
            user.AccountStatus = AccountStatus.Banned;
        }
        else
        {
            // Reject -> not a warning; restore the account if it was only flagged for review.
            entry.ReviewStatus = BlacklistReviewStatus.Rejected;
            entry.IsActive = false;
            if (user.AccountStatus == AccountStatus.PendingBanReview)
                user.AccountStatus = AccountStatus.Active;
        }

        entry.ReviewedByUserId = request.ReviewedByUserId;
        if (!string.IsNullOrWhiteSpace(request.Note))
            entry.InternalNote = request.Note.Trim();

        _audit.Write(
            action: request.Confirm ? "Blacklist.Confirmed" : "Blacklist.Rejected",
            entityType: "BlacklistEntry",
            entityId: entry.BlacklistEntryId.ToString("N"),
            actorUserId: request.ReviewedByUserId,
            before: new { ReviewStatus = fromReview.ToString(), AccountStatus = fromAccount.ToString() },
            after: new
            {
                ReviewStatus = entry.ReviewStatus.ToString(),
                entry.IsActive,
                AccountStatus = user.AccountStatus.ToString(),
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Blacklist entry {EntryId} reviewed (confirm={Confirm}) by {Admin}: account {From} -> {To}.",
            entry.BlacklistEntryId, request.Confirm, request.ReviewedByUserId, fromAccount, user.AccountStatus);

        return Result<BlacklistEntryDto>.Success(ToDto(entry));
    }

    /// <summary>
    /// Admin: lift an active CONFIRMED entry — sets IsActive=false, ReviewStatus=Overturned, and
    /// restores the account to Active if it was Banned. Audited (FR-24).
    /// </summary>
    public async Task<Result<BlacklistEntryDto>> UnbanAsync(Guid blacklistEntryId, Guid reviewedByUserId, string? note, CancellationToken ct = default)
    {
        var entry = await _db.BlacklistEntries
            .FirstOrDefaultAsync(e => e.BlacklistEntryId == blacklistEntryId, ct);
        if (entry is null)
            return Result<BlacklistEntryDto>.Fail("Blacklist entry not found.");

        if (!entry.IsActive || entry.ReviewStatus != BlacklistReviewStatus.Confirmed)
            return Result<BlacklistEntryDto>.Fail("Only an active confirmed entry can be lifted.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == entry.UserId, ct);
        if (user is null)
            return Result<BlacklistEntryDto>.Fail("User not found.");

        var fromReview = entry.ReviewStatus;
        var fromAccount = user.AccountStatus;

        entry.IsActive = false;
        entry.ReviewStatus = BlacklistReviewStatus.Overturned;
        entry.ReviewedByUserId = reviewedByUserId;
        if (!string.IsNullOrWhiteSpace(note))
            entry.InternalNote = note.Trim();

        // Restore the account when this lift removes the ban. (We only flip Banned -> Active here; if
        // other active confirmed entries remain, an Admin lifts those separately.)
        if (user.AccountStatus == AccountStatus.Banned)
            user.AccountStatus = AccountStatus.Active;

        _audit.Write(
            action: "Blacklist.Lifted",
            entityType: "BlacklistEntry",
            entityId: entry.BlacklistEntryId.ToString("N"),
            actorUserId: reviewedByUserId,
            before: new { ReviewStatus = fromReview.ToString(), AccountStatus = fromAccount.ToString(), IsActive = true },
            after: new
            {
                ReviewStatus = entry.ReviewStatus.ToString(),
                entry.IsActive,
                AccountStatus = user.AccountStatus.ToString(),
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Blacklist entry {EntryId} lifted by {Admin}: account {From} -> {To}.",
            entry.BlacklistEntryId, reviewedByUserId, fromAccount, user.AccountStatus);

        return Result<BlacklistEntryDto>.Success(ToDto(entry));
    }

    private static BlacklistEntryDto ToDto(BlacklistEntry e) => new(
        e.BlacklistEntryId,
        e.UserId,
        e.ReasonCodeId,
        e.ReviewStatus.ToString(),
        e.AppealStatus.ToString(),
        e.IsActive,
        e.EffectiveAtUtc,
        e.ExpiresAtUtc);
}
