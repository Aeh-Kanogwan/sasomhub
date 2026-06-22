using Marketplace.Application.Common;

namespace Marketplace.Application.Trades;

// FR-18/FR-19: no-touch direct transfer. The platform NEVER holds money:
// a Transaction is only a RECORD of an off-platform transfer. There is no
// wallet/balance/escrow/refund anywhere in this flow.

/// <summary>Off-platform transfer note the buyer attaches after paying the seller directly (FR-18).</summary>
public record MarkTransferredRequest(Guid TransactionId, Guid BuyerId, string? ExternalPaymentNote);

/// <summary>Buyer confirms goods received -> transaction Confirmed, trust recalculated (FR-19).</summary>
public record ConfirmReceiptRequest(Guid TransactionId, Guid BuyerId);

public record TransactionDto(
    Guid TransactionId,
    Guid ProductId,
    Guid SellerId,
    Guid BuyerId,
    Guid? WinningBidId,
    decimal AgreedAmount,
    string Currency,
    string Status);

/// <summary>No-touch trade service (FR-18/FR-19). Records direct transfers only — never money movement.</summary>
public interface ITradeService
{
    /// <summary>
    /// FR-12/FR-18: open a no-touch transfer record (Status=Pending) for a closed deal.
    /// Called by AuctionService on auction close, or directly for a fixed-price agreement.
    /// </summary>
    Task<Result<TransactionDto>> CreateNoTouchTransactionAsync(
        Guid productId, Guid sellerId, Guid buyerId, Guid? winningBidId, decimal agreedAmount,
        CancellationToken ct = default);

    /// <summary>FR-18: buyer marks they transferred money directly to the seller (off-platform).</summary>
    Task<Result<TransactionDto>> MarkTransferredAsync(MarkTransferredRequest request, CancellationToken ct = default);

    /// <summary>FR-19: buyer confirms goods received -> Confirmed; triggers trust-score adjustment for both parties.</summary>
    Task<Result<TransactionDto>> ConfirmReceiptAsync(ConfirmReceiptRequest request, CancellationToken ct = default);
}
