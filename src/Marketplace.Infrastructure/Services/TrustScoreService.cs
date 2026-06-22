using Marketplace.Application.Common;
using Marketplace.Application.Reputation;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// FR-13..FR-16 Trust Score engine.
///
/// Scoring model:
///   - Every user starts at 100 (TrustScore.Score default, FR-13) with an "initial" history row.
///   - Each scored event applies a Delta and clamps the result to [0,100] (matches CK_TrustScore_Range).
///   - Delta comes from the standard reason code (BlacklistReasonCode.DefaultScorePenalty, Legal #3),
///     or an explicit override. Minor offenses (Severity 1-2) are -20 by seed (FR-14).
///
/// Auto-penalty (FR-15) — graduated, with due process:
///   - Score &lt; 60                       => AccountStatus.Suspended (reversible, appealable).
///   - Severe reason (Severity &gt;= 4,
///     e.g. counterfeit / payment fraud) => PROPOSE ban: AccountStatus.PendingBanReview
///                                          (NOT Banned) — an Admin must confirm before it is
///                                          permanent (DP-3, human-in-the-loop). We also open a
///                                          PendingReview internal BlacklistEntry (warn-on-deal,
///                                          FR-17) that is likewise unconfirmed until an Admin acts.
///   - Already-Banned users are never auto-downgraded/upgraded here.
///
/// History is APPEND-ONLY (Legal): we only ever Add() TrustScoreHistory rows, never update/delete.
/// </summary>
public class TrustScoreService : ITrustScoreService
{
    public const int InitialScore = 100;
    public const int SuspendThreshold = 60;        // FR-15: &lt; 60 => Suspended
    public const byte SevereSeverityThreshold = 4; // Severity 4-5 => propose ban + blacklist

    private readonly MarketplaceDbContext _db;
    private readonly ILogger<TrustScoreService> _logger;

    public TrustScoreService(MarketplaceDbContext db, ILogger<TrustScoreService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<TrustScoreDto>> EnsureInitializedAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await _db.TrustScores.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId, ct);
        if (existing is not null)
            return Result<TrustScoreDto>.Success(ToDto(existing));

        var userExists = await _db.Users.AnyAsync(u => u.UserId == userId, ct);
        if (!userExists)
            return Result<TrustScoreDto>.Fail("User not found.");

        var now = DateTime.UtcNow;
        var score = new TrustScore { UserId = userId, Score = InitialScore, LastCalculatedAtUtc = now };
        _db.TrustScores.Add(score);
        // FR-13: first history row, reason = initial (no reason code; system actor).
        _db.TrustScoreHistory.Add(new TrustScoreHistory
        {
            UserId = userId,
            Delta = InitialScore,
            ScoreAfter = InitialScore,
            ReasonCodeId = null,
            Note = "initial",
            CreatedByUserId = null,
            CreatedAtUtc = now
        });
        await _db.SaveChangesAsync(ct);
        return Result<TrustScoreDto>.Success(ToDto(score));
    }

    public async Task<Result<TrustScoreDto>> ApplyEventAsync(ApplyTrustEventRequest request, CancellationToken ct = default)
    {
        var reason = await _db.BlacklistReasonCodes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReasonCodeId == request.ReasonCodeId, ct);
        if (reason is null)
            return Result<TrustScoreDto>.Fail("Unknown reason code.");

        var score = await _db.TrustScores.FirstOrDefaultAsync(t => t.UserId == request.UserId, ct);
        if (score is null)
        {
            var init = await EnsureInitializedAsync(request.UserId, ct);
            if (!init.Succeeded)
                return Result<TrustScoreDto>.Fail(init.Error ?? "Cannot initialize trust score.");
            score = await _db.TrustScores.FirstAsync(t => t.UserId == request.UserId, ct);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == request.UserId, ct);
        if (user is null)
            return Result<TrustScoreDto>.Fail("User not found.");

        var delta = request.DeltaOverride ?? reason.DefaultScorePenalty;
        var now = DateTime.UtcNow;
        var newScore = Math.Clamp(score.Score + delta, 0, 100);

        score.Score = newScore;
        score.LastCalculatedAtUtc = now;

        // Append-only history (FR-14): {delta, reason, refId, actor, timestamp}.
        _db.TrustScoreHistory.Add(new TrustScoreHistory
        {
            UserId = request.UserId,
            Delta = delta,
            ScoreAfter = newScore,
            ReasonCodeId = request.ReasonCodeId,
            RelatedTransactionId = request.RelatedTransactionId,
            Note = request.Note,
            CreatedByUserId = request.ActorUserId,
            CreatedAtUtc = now
        });

        ApplyAutoPenalty(user, reason, newScore, request, now);

        await _db.SaveChangesAsync(ct);
        return Result<TrustScoreDto>.Success(ToDto(score));
    }

    public async Task<Result<IReadOnlyList<TrustScoreHistoryDto>>> GetHistoryAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _db.TrustScoreHistory.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAtUtc)
            .Select(h => new TrustScoreHistoryDto(
                h.TrustScoreHistoryId, h.Delta, h.ScoreAfter, h.ReasonCodeId,
                h.RelatedTransactionId, h.Note, h.CreatedAtUtc))
            .ToListAsync(ct);
        return Result<IReadOnlyList<TrustScoreHistoryDto>>.Success(rows);
    }

    public async Task<Result> RecalculateAsync(Guid userId, CancellationToken ct = default)
    {
        // Worker entry point. We do NOT recompute the score from scratch (append-only history is
        // the ledger of truth; re-summing would risk double counting). Instead we re-assert the
        // status rule against the current score so a user whose score dropped below 60 outside an
        // ApplyEvent path is still Suspended, and an appeal-restored Active is respected.
        var score = await _db.TrustScores.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId, ct);
        if (score is null)
            return Result.Fail("No trust score for user.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null)
            return Result.Fail("User not found.");

        // Only the reversible suspension rule is auto-applied here; ban proposals stay where an
        // Admin left them (never auto-permaban, never auto-clear PendingBanReview/Banned).
        if (user.AccountStatus == AccountStatus.Active && score.Score < SuspendThreshold)
        {
            user.AccountStatus = AccountStatus.Suspended;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("TrustScoreRecalc: user {UserId} suspended (score {Score} < {Threshold}).",
                userId, score.Score, SuspendThreshold);
        }

        return Result.Success();
    }

    /// <summary>FR-15 graduated auto-penalty with due process. Mutates <paramref name="user"/>; caller saves.</summary>
    private void ApplyAutoPenalty(User user, BlacklistReasonCode reason, int newScore,
        ApplyTrustEventRequest request, DateTime now)
    {
        // Never touch users already banned/under ban review — only an Admin moves them.
        if (user.AccountStatus is AccountStatus.Banned or AccountStatus.PendingBanReview)
            return;

        var severe = reason.Severity >= SevereSeverityThreshold;

        if (severe)
        {
            // Propose ban (NOT permanent) + open an unconfirmed internal blacklist entry (FR-17).
            user.AccountStatus = AccountStatus.PendingBanReview;
            user.UpdatedAtUtc = now;

            _db.PenaltyActions.Add(new PenaltyAction
            {
                UserId = user.UserId,
                ActionType = PenaltyActionType.Ban,            // proposed; effective only after Admin confirms
                ReasonCodeId = reason.ReasonCodeId,
                RelatedTransactionId = request.RelatedTransactionId,
                IssuedByUserId = null,                         // null => auto
                EffectiveAtUtc = now,
                CreatedAtUtc = now
            });

            _db.BlacklistEntries.Add(new BlacklistEntry
            {
                UserId = user.UserId,
                ReasonCodeId = reason.ReasonCodeId,
                ReviewStatus = BlacklistReviewStatus.PendingReview, // due process: not active until confirmed
                AppealStatus = BlacklistAppealStatus.None,
                IsActive = false,                              // warn-on-deal only after Admin confirms
                CreatedByUserId = null,
                EffectiveAtUtc = now,
                CreatedAtUtc = now,
                InternalNote = request.Note
            });

            _logger.LogWarning("TrustScore: PROPOSED ban+blacklist for user {UserId} (reason {Code}, severity {Sev}).",
                user.UserId, reason.Code, reason.Severity);
            return;
        }

        if (newScore < SuspendThreshold && user.AccountStatus == AccountStatus.Active)
        {
            // Reversible, appealable suspension (FR-15).
            user.AccountStatus = AccountStatus.Suspended;
            user.UpdatedAtUtc = now;
            _db.PenaltyActions.Add(new PenaltyAction
            {
                UserId = user.UserId,
                ActionType = PenaltyActionType.Suspend,
                ReasonCodeId = reason.ReasonCodeId,
                RelatedTransactionId = request.RelatedTransactionId,
                IssuedByUserId = null,
                EffectiveAtUtc = now,
                CreatedAtUtc = now
            });
            _logger.LogInformation("TrustScore: user {UserId} suspended (score {Score}).", user.UserId, newScore);
        }
    }

    private static TrustScoreDto ToDto(TrustScore t) => new(t.UserId, t.Score, t.LastCalculatedAtUtc);
}
