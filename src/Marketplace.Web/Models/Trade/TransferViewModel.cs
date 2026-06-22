namespace Marketplace.Web.Models.Trade;

/// <summary>
/// FR-18 (NO-TOUCH): shows the SELLER's bank details for a direct buyer->seller transfer, plus the
/// prominent "platform does not receive/hold/intermediate funds" banner. FR-19: confirm-receipt + review.
/// LEGAL INVARIANT: this view model carries NO wallet/escrow/held-amount field, and the platform NEVER
/// accepts money. AgreedAmount is reference-only (fee calc / display), never a balance.
/// </summary>
public class TransferViewModel
{
    public Guid TransactionId { get; init; }

    // Order summary
    public string ProductTitle { get; init; } = string.Empty;
    public string? GradeLabel { get; init; }
    public decimal AgreedAmount { get; init; }
    public string Currency { get; init; } = "THB";

    /// <summary>Seller's off-platform bank details to display for direct transfer (FR-18).
    /// Account number should already be masked per PDPA where appropriate.</summary>
    public SellerBankInfo SellerBank { get; init; } = new();

    /// <summary>Current no-touch transaction status (Pending/Transferred/Confirmed/...).</summary>
    public Marketplace.Domain.Enums.TransactionStatus Status { get; init; }
}

public class SellerBankInfo
{
    public string BankName { get; init; } = string.Empty;
    public string AccountNumberMasked { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
}
