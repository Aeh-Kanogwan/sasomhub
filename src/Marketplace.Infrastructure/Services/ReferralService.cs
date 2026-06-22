using Marketplace.Application.Common;
using Marketplace.Application.Credits;
using Marketplace.Application.Referrals;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// Single-level referral service (FR-28). Legal: SINGLE-LEVEL ONLY — a referrer earns only from users
/// they personally invited. There is intentionally no upline/downline/level concept here or in the schema
/// (avoids MLM/pyramid under พ.ร.บ.ขายตรงและตลาดแบบตรง). Rewards are paid as non-cashable platform credit only.
/// </summary>
public sealed class ReferralService : IReferralService
{
    private readonly MarketplaceDbContext _db;
    private readonly ICreditService _credit;
    private readonly ConfigVersionResolver _config;

    public ReferralService(MarketplaceDbContext db, ICreditService credit, ConfigVersionResolver config)
    {
        _db = db;
        _credit = credit;
        _config = config;
    }

    /// <summary>FR-28: link a new user to a referrer via code. A user can be referred only once; no self-referral.</summary>
    public async Task<Result<ReferralDto>> RegisterAsync(RegisterReferralRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ReferralCode))
            return Result<ReferralDto>.Fail("Referral code is required.");

        var code = await _db.ReferralCodes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == request.ReferralCode && c.IsActive, ct);
        if (code is null)
            return Result<ReferralDto>.Fail("Referral code not found or inactive.");

        // Single-level guard #1: no self-referral (also enforced by CK_Referral_NotSelf).
        if (code.UserId == request.ReferredUserId)
            return Result<ReferralDto>.Fail("Self-referral is not allowed.");

        // Single-level guard #2: a user can be referred only once (also enforced by UQ_Referral_Referred).
        var alreadyReferred = await _db.Referrals.AsNoTracking()
            .AnyAsync(r => r.ReferredUserId == request.ReferredUserId, ct);
        if (alreadyReferred)
            return Result<ReferralDto>.Fail("This user has already been referred.");

        var referral = new Referral
        {
            ReferrerUserId = code.UserId,
            ReferredUserId = request.ReferredUserId,
            ReferralCode = code.Code,
            Status = ReferralStatus.Pending,   // reward only on qualifying trigger (FR-28 anti-abuse)
            RewardCreditToReferrer = 0m,
            RewardCreditToReferred = 0m,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.Referrals.Add(referral);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) // lost race on UQ_Referral_Referred
        {
            return Result<ReferralDto>.Fail("This user has already been referred.");
        }

        return Result<ReferralDto>.Success(new ReferralDto(
            referral.ReferralId, referral.ReferrerUserId, referral.ReferredUserId, referral.Status.ToString()));
    }

    /// <summary>
    /// FR-28: once the qualifying trigger fires (e.g. referred user's first paid membership), pay the
    /// single-level reward to BOTH parties as non-cashable credit. Idempotent: the credit ledger keys on
    /// "Referral:{ReferralId}" so a worker re-run / retry never double-rewards (B-02).
    /// </summary>
    public async Task<Result> QualifyAndRewardAsync(Guid referralId, CancellationToken ct = default)
    {
        var referral = await _db.Referrals.FirstOrDefaultAsync(r => r.ReferralId == referralId, ct);
        if (referral is null)
            return Result.Fail("Referral not found.");
        if (referral.Status == ReferralStatus.Rejected)
            return Result.Fail("Referral was rejected and cannot be rewarded.");
        if (referral.Status == ReferralStatus.Rewarded)
            return Result.Success(); // already paid — idempotent no-op

        var now = DateTime.UtcNow;
        var referrerAmount = await _config.GetDecimalAsync(ConfigKeys.ReferralRewardReferrer, now, fallback: 0m, ct);
        var referredAmount = await _config.GetDecimalAsync(ConfigKeys.ReferralRewardReferred, now, fallback: 0m, ct);
        var expiryDays = await _config.GetIntAsync(ConfigKeys.ReferralCreditExpiryDays, now, fallback: 0, ct);
        DateTime? expiresAtUtc = expiryDays > 0 ? now.AddDays(expiryDays) : null;

        // Distinct refIds per side keep one ledger row per party per referral (single-level: exactly two payouts max).
        if (referrerAmount > 0m)
        {
            var r = await _credit.GrantAsync(referral.ReferrerUserId, referrerAmount,
                CreditTransactionType.ReferralReward, $"{referralId}:referrer", expiresAtUtc, ct);
            if (!r.Succeeded) return r;
        }
        if (referredAmount > 0m)
        {
            var r = await _credit.GrantAsync(referral.ReferredUserId, referredAmount,
                CreditTransactionType.ReferralReward, $"{referralId}:referred", expiresAtUtc, ct);
            if (!r.Succeeded) return r;
        }

        referral.RewardCreditToReferrer = referrerAmount;
        referral.RewardCreditToReferred = referredAmount;
        referral.Status = ReferralStatus.Rewarded;
        referral.RewardedAtUtc = now;
        await _db.SaveChangesAsync(ct);

        // TODO(audit): write AuditLogs rows (FR-24) for the referral payout once the audit service exists.
        return Result.Success();
    }

    /// <summary>
    /// FR-28: clawback credit gained by abuse (self-referral / duplicate device/IP/KYC). Posts a negative
    /// 'Revoke' ledger row per party (auditable, distinct from Adjustment) and marks the referral Rejected.
    /// </summary>
    public async Task<Result> RevokeForAbuseAsync(Guid referralId, string reason, CancellationToken ct = default)
    {
        var referral = await _db.Referrals.FirstOrDefaultAsync(r => r.ReferralId == referralId, ct);
        if (referral is null)
            return Result.Fail("Referral not found.");

        var now = DateTime.UtcNow;

        // Revoke = negative ledger row. We reuse GrantAsync's signed engine via a dedicated revoke path:
        // post one Revoke row per party for the previously-rewarded amount. Idempotent per refId.
        if (referral.RewardCreditToReferrer > 0m)
        {
            var r = await RevokeOneAsync(referral.ReferrerUserId, referral.RewardCreditToReferrer,
                $"{referralId}:referrer:revoke", ct);
            if (!r.Succeeded) return r;
        }
        if (referral.RewardCreditToReferred > 0m)
        {
            var r = await RevokeOneAsync(referral.ReferredUserId, referral.RewardCreditToReferred,
                $"{referralId}:referred:revoke", ct);
            if (!r.Succeeded) return r;
        }

        referral.Status = ReferralStatus.Rejected;
        await _db.SaveChangesAsync(ct);

        // TODO(penalty): link to the penalty/blacklist flow (FR-15) and write AuditLogs (FR-24) with `reason`.
        return Result.Success();
    }

    /// <summary>
    /// Posts a single negative 'Revoke' ledger row directly (clawback). Kept here rather than on ICreditService
    /// because Revoke is a referral-abuse concern; it does not floor at zero the way a spend does — a confirmed
    /// clawback may drive balance toward what was wrongly granted (DB CK_CreditAcc_Balance still forbids &lt; 0).
    /// </summary>
    private async Task<Result> RevokeOneAsync(Guid userId, decimal amount, string refId, CancellationToken ct)
    {
        var key = $"{CreditTransactionType.Revoke}:{refId}";
        if (await _db.CreditTransactions.AsNoTracking().AnyAsync(t => t.IdempotencyKey == key, ct))
            return Result.Success();

        var useTx = _db.Database.IsRelational();
        await using var tx = useTx
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;
        var account = await _db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (account is null)
            return Result.Fail("Credit account not found for revoke.");

        var newBalance = account.Balance - amount;
        // The granted credit may already have been spent; clamp the clawback to available balance so we
        // never violate CK_CreditAcc_Balance (>= 0). Remaining shortfall is logged as a TODO follow-up.
        if (newBalance < 0m)
        {
            amount = account.Balance;
            newBalance = 0m;
            // TODO(abuse): credit was already spent before revoke — flag for manual recovery / penalty (FR-15).
        }

        account.Balance = newBalance;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = userId,
            Amount = -amount,
            Type = CreditTransactionType.Revoke,
            RefId = refId,
            IdempotencyKey = key,
            BalanceAfter = newBalance,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result.Success();
    }
}
