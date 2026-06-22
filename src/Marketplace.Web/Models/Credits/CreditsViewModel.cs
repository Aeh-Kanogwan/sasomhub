using Marketplace.Domain.Enums;

namespace Marketplace.Web.Models.Credits;

/// <summary>
/// FR-28/FR-29: credit wallet (balance hero + transparent append-only ledger + referral CTA).
/// LEGAL INVARIANT: credit is non-cashable, non-transferable — there is NO withdraw/cash-out/transfer
/// action anywhere in this model or its view (outside พ.ร.บ.ระบบการชำระเงิน 2560).
/// Maps to ICreditService.GetBalanceAsync / GetLedgerAsync (CreditBalanceDto / CreditLedgerEntryDto).
/// </summary>
public class CreditsViewModel
{
    public decimal Balance { get; init; }

    /// <summary>FR-28: the user's own referral code (single-level) for the "invite friends" CTA.</summary>
    public string? ReferralCode { get; init; }

    public IReadOnlyList<CreditLedgerRow> Ledger { get; init; } = new List<CreditLedgerRow>();
}

/// <summary>One ledger row (earn/spend/expire/revoke). Append-only; never edited (FR-37).</summary>
public record CreditLedgerRow(
    DateTime Date,
    string Description,
    CreditTransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    DateTime? ExpiresAtUtc);
