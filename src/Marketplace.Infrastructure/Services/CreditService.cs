using Marketplace.Application.Common;
using Marketplace.Application.Credits;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// Non-cashable platform-credit ledger (FR-28/29/30). Credit is non-cashable, non-transferable and
/// expirable; it is spendable ONLY on platform services (listing promotion). By design this class has
/// NO withdraw/cash-out/transfer path — that requirement is legal (พ.ร.บ.ระบบการชำระเงิน 2560), do not add one.
///
/// Ledger is append-only: every grant/spend/expire/revoke writes a <see cref="CreditTransaction"/> row and
/// updates <see cref="CreditAccount.Balance"/> in the same SQL transaction (FR-30 atomicity). Idempotency
/// (B-02/G-2) is enforced two ways at the DB layer: the unique <c>IdempotencyKey</c> and the unique
/// <c>(Type, RefId)</c> — so a retry / double-click / worker re-run cannot double-credit or double-spend.
/// </summary>
public sealed class CreditService : ICreditService
{
    private readonly MarketplaceDbContext _db;

    public CreditService(MarketplaceDbContext db) => _db = db;

    /// <summary>FR-28: grant earned credit (positive ledger row) with optional expiry. Idempotent per (Type, refId).</summary>
    public Task<Result> GrantAsync(Guid userId, decimal amount, CreditTransactionType type, string? refId,
        DateTime? expiresAtUtc, CancellationToken ct = default)
    {
        if (amount <= 0)
            return Task.FromResult(Result.Fail("Grant amount must be positive."));
        if (type is CreditTransactionType.PromoSpend or CreditTransactionType.Expiry)
            return Task.FromResult(Result.Fail($"'{type}' is not a grant type."));

        // refId is the idempotency anchor for a grant (e.g. "Referral:{ReferralId}").
        var key = refId is null ? null : $"{type}:{refId}";
        return ApplyDeltaAsync(userId, amount, type, refId, key, expiresAtUtc, note: null, ct);
    }

    /// <summary>FR-30: spend credit on a platform service (PromoSpend). Rejects if balance insufficient. Idempotent per refId.</summary>
    public Task<Result> SpendAsync(Guid userId, decimal amount, string refId, CancellationToken ct = default)
    {
        if (amount <= 0)
            return Task.FromResult(Result.Fail("Spend amount must be positive."));
        if (string.IsNullOrWhiteSpace(refId))
            return Task.FromResult(Result.Fail("refId is required for spend (idempotency anchor)."));

        var key = $"{CreditTransactionType.PromoSpend}:{refId}";
        return ApplyDeltaAsync(userId, -amount, CreditTransactionType.PromoSpend, refId, key,
            expiresAtUtc: null, note: null, ct);
    }

    /// <summary>FR-28: expire credit past ExpiresAtUtc (negative Expiry ledger row). Driven by the expiry worker.</summary>
    public Task<Result> ExpireAsync(Guid userId, decimal amount, string? refId, CancellationToken ct = default)
    {
        if (amount <= 0)
            return Task.FromResult(Result.Fail("Expire amount must be positive."));

        var key = refId is null ? null : $"{CreditTransactionType.Expiry}:{refId}";
        return ApplyDeltaAsync(userId, -amount, CreditTransactionType.Expiry, refId, key,
            expiresAtUtc: null, note: null, ct);
    }

    public async Task<Result<CreditBalanceDto>> GetBalanceAsync(Guid userId, CancellationToken ct = default)
    {
        var account = await _db.CreditAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId, ct);
        // No account row yet == zero balance (account is created lazily on first grant).
        return Result<CreditBalanceDto>.Success(new CreditBalanceDto(userId, account?.Balance ?? 0m));
    }

    public async Task<Result<IReadOnlyList<CreditLedgerEntryDto>>> GetLedgerAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _db.CreditTransactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new CreditLedgerEntryDto(
                t.CreditTransactionId, t.Amount, t.Type, t.BalanceAfter, t.ExpiresAtUtc, t.CreatedAtUtc))
            .ToListAsync(ct);
        return Result<IReadOnlyList<CreditLedgerEntryDto>>.Success(rows);
    }

    /// <summary>
    /// Core atomic mutation: load/create the account, post one append-only ledger row, update the cached balance —
    /// all inside one DB transaction. <paramref name="delta"/> is signed (positive = earn, negative = spend/expire).
    /// Idempotency is enforced by the unique indexes; on a duplicate insert we treat it as already-applied (success).
    /// </summary>
    private async Task<Result> ApplyDeltaAsync(Guid userId, decimal delta, CreditTransactionType type,
        string? refId, string? idempotencyKey, DateTime? expiresAtUtc, string? note, CancellationToken ct)
    {
        // Fast pre-check: if this exact logical op already produced a ledger row, no-op (idempotent retry).
        if (idempotencyKey is not null)
        {
            var already = await _db.CreditTransactions.AsNoTracking()
                .AnyAsync(t => t.IdempotencyKey == idempotencyKey, ct);
            if (already) return Result.Success();
        }

        // Serializable so the balance read-modify-write and the duplicate check are race-free against
        // concurrent grant/spend on the same account (paired with the unique idempotency indexes).
        // Skip the explicit transaction for providers that don't support it (e.g. EF InMemory in tests).
        var useTx = _db.Database.IsRelational();
        await using var tx = useTx
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;
        try
        {
            var account = await _db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);
            if (account is null)
            {
                account = new CreditAccount { UserId = userId, Balance = 0m };
                _db.CreditAccounts.Add(account);
            }

            var newBalance = account.Balance + delta;
            if (newBalance < 0m)
                return Result.Fail("Insufficient credit balance."); // tx disposed -> rolled back

            account.Balance = newBalance;
            account.UpdatedAtUtc = DateTime.UtcNow;

            _db.CreditTransactions.Add(new CreditTransaction
            {
                UserId = userId,
                Amount = delta,
                Type = type,
                RefId = refId,
                IdempotencyKey = idempotencyKey,
                BalanceAfter = newBalance,
                ExpiresAtUtc = expiresAtUtc,
                Note = note,
                CreatedAtUtc = DateTime.UtcNow,
            });

            // TODO(audit): write an AuditLogs row (FR-24/FR-29) for this credit op once the audit
            // service exists — credit grants/spends/expiries MUST be auditable.

            await _db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Lost the idempotency race (UX_CreditTx_Idempotency / UX_CreditTx_TypeRef): another
            // concurrent attempt already posted this exact op. Treat as already-applied.
            if (tx is not null) await tx.RollbackAsync(ct);
            return Result.Success();
        }
    }

    /// <summary>Detects a SQL Server unique-index violation (2601/2627) so we can map it to idempotent success.</summary>
    private static bool IsDuplicateKey(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.SqlClient.SqlException sql
           && (sql.Number == 2601 || sql.Number == 2627);
}
