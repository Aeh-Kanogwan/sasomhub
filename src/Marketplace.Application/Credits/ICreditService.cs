using Marketplace.Application.Common;
using Marketplace.Domain.Enums;

namespace Marketplace.Application.Credits;

// FR-28/29/30: platform credit is non-cashable, non-transferable, expirable. Used only for
// platform services (e.g. listing promotions). NO withdraw/cash-out/transfer operations exist
// by design (outside พ.ร.บ.ระบบการชำระเงิน 2560).

public record CreditBalanceDto(Guid UserId, decimal Balance);

public record CreditLedgerEntryDto(
    long CreditTransactionId,
    decimal Amount,
    CreditTransactionType Type,
    decimal BalanceAfter,
    DateTime? ExpiresAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// Non-cashable platform credit ledger service (FR-29). Skeleton.
/// IMPORTANT: this interface intentionally has NO Withdraw/CashOut/Transfer method.
/// </summary>
public interface ICreditService
{
    /// <summary>FR-28: grant earned credit (e.g. referral reward) with optional expiry; append ledger row.</summary>
    Task<Result> GrantAsync(Guid userId, decimal amount, CreditTransactionType type, string? refId, DateTime? expiresAtUtc, CancellationToken ct = default);

    /// <summary>FR-30: spend credit on a platform service (PromoSpend). Rejects if balance insufficient.</summary>
    Task<Result> SpendAsync(Guid userId, decimal amount, string refId, CancellationToken ct = default);

    /// <summary>FR-28: expire credit past ExpiresAtUtc (negative Expiry ledger row). Driven by worker.</summary>
    Task<Result> ExpireAsync(Guid userId, decimal amount, string? refId, CancellationToken ct = default);

    Task<Result<CreditBalanceDto>> GetBalanceAsync(Guid userId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CreditLedgerEntryDto>>> GetLedgerAsync(Guid userId, CancellationToken ct = default);
}
