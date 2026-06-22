using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Reputation;
using Marketplace.Application.Trades;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// FR-18 / FR-19 — NO-TOUCH trade flow.
///
/// LEGAL #1 (พ.ร.บ.ระบบการชำระเงิน 2560): the platform NEVER receives, holds, escrows or
/// refunds buyer/seller money. A Transaction is purely a RECORD of an off-platform direct
/// transfer. There is intentionally no wallet/balance/escrow/held/refunded concept anywhere
/// in this service. AgreedAmount is reference data only (used elsewhere for fee calc on
/// platform-revenue, never for moving money between users).
///
/// Status flow (CK_Tx_Status): Pending -> Transferred -> Confirmed | Disputed | Cancelled.
/// Each transition appends a TransactionStatusHistory row.
/// </summary>
public class TradeService : ITradeService
{
    private readonly MarketplaceDbContext _db;
    private readonly ITrustScoreService _trustScore;
    private readonly IAuditService _audit;
    private readonly ILogger<TradeService> _logger;

    public TradeService(MarketplaceDbContext db, ITrustScoreService trustScore, IAuditService audit, ILogger<TradeService> logger)
    {
        _db = db;
        _trustScore = trustScore;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<TransactionDto>> CreateNoTouchTransactionAsync(
        Guid productId, Guid sellerId, Guid buyerId, Guid? winningBidId, decimal agreedAmount,
        CancellationToken ct = default)
    {
        if (buyerId == sellerId)
            return Result<TransactionDto>.Fail("Buyer and seller must differ (CK_Tx_Parties).");
        if (agreedAmount < 0)
            return Result<TransactionDto>.Fail("AgreedAmount must be >= 0.");

        // Idempotency: one no-touch transaction per winning bid (auction close may be retried by worker).
        if (winningBidId is Guid wbid)
        {
            var existing = await _db.Transactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.WinningBidId == wbid, ct);
            if (existing is not null)
                return Result<TransactionDto>.Success(ToDto(existing));
        }

        var now = DateTime.UtcNow;
        var tx = new Transaction
        {
            ProductId = productId,
            SellerId = sellerId,
            BuyerId = buyerId,
            WinningBidId = winningBidId,
            AgreedAmount = agreedAmount,
            Currency = "THB",
            Status = TransactionStatus.Pending,
            // No-touch reminder persisted with the record itself.
            ExternalPaymentNote = "Off-platform direct transfer only. Platform does not receive, hold or refund funds.",
            CreatedAtUtc = now
        };
        _db.Transactions.Add(tx);
        _db.TransactionStatusHistory.Add(new TransactionStatusHistory
        {
            Transaction = tx,
            FromStatus = null,
            ToStatus = nameof(TransactionStatus.Pending),
            ChangedByUserId = null, // system/worker
            Note = "Deal opened (no-touch). Buyer to pay seller directly off-platform.",
            ChangedAtUtc = now
        });

        // FR-24: audit the no-touch deal opening. AgreedAmount is reference-only — the platform never
        // receives/holds funds (LEGAL #1), so this records a deal, not a money movement.
        _audit.Write(
            action: "Transaction.NoTouchOpened",
            entityType: "Transaction",
            entityId: tx.TransactionId.ToString("N"),
            actorUserId: null, // opened by the auction-close worker / system
            after: new { tx.TransactionId, tx.ProductId, tx.SellerId, tx.BuyerId, tx.WinningBidId, tx.AgreedAmount, Status = tx.Status.ToString() });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("No-touch transaction {TxId} opened for product {ProductId}.", tx.TransactionId, productId);
        return Result<TransactionDto>.Success(ToDto(tx));
    }

    public async Task<Result<TransactionDto>> MarkTransferredAsync(MarkTransferredRequest request, CancellationToken ct = default)
    {
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == request.TransactionId, ct);
        if (tx is null)
            return Result<TransactionDto>.Fail("Transaction not found.");
        if (tx.BuyerId != request.BuyerId)
            return Result<TransactionDto>.Fail("Only the buyer can mark a direct transfer.");
        if (tx.Status != TransactionStatus.Pending)
            return Result<TransactionDto>.Fail($"Cannot mark transfer from status {tx.Status}.");

        var now = DateTime.UtcNow;
        var from = tx.Status.ToString();
        tx.Status = TransactionStatus.Transferred;
        tx.TransferredAtUtc = now;
        // Buyer's own note about the off-platform transfer (e.g. bank slip ref). No money touches the platform.
        if (!string.IsNullOrWhiteSpace(request.ExternalPaymentNote))
            tx.ExternalPaymentNote = request.ExternalPaymentNote;

        _db.TransactionStatusHistory.Add(new TransactionStatusHistory
        {
            TransactionId = tx.TransactionId,
            FromStatus = from,
            ToStatus = nameof(TransactionStatus.Transferred),
            ChangedByUserId = request.BuyerId,
            Note = "Buyer reports direct off-platform transfer to seller.",
            ChangedAtUtc = now
        });

        await _db.SaveChangesAsync(ct);
        return Result<TransactionDto>.Success(ToDto(tx));
    }

    public async Task<Result<TransactionDto>> ConfirmReceiptAsync(ConfirmReceiptRequest request, CancellationToken ct = default)
    {
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.TransactionId == request.TransactionId, ct);
        if (tx is null)
            return Result<TransactionDto>.Fail("Transaction not found.");
        if (tx.BuyerId != request.BuyerId)
            return Result<TransactionDto>.Fail("Only the buyer can confirm receipt.");
        if (tx.Status is not (TransactionStatus.Transferred or TransactionStatus.Pending))
            return Result<TransactionDto>.Fail($"Cannot confirm receipt from status {tx.Status}.");

        var now = DateTime.UtcNow;
        var from = tx.Status.ToString();
        tx.Status = TransactionStatus.Confirmed;
        tx.ConfirmedAtUtc = now;

        _db.TransactionStatusHistory.Add(new TransactionStatusHistory
        {
            TransactionId = tx.TransactionId,
            FromStatus = from,
            ToStatus = nameof(TransactionStatus.Confirmed),
            ChangedByUserId = request.BuyerId,
            Note = "Buyer confirmed goods received (FR-19).",
            ChangedAtUtc = now
        });

        // Mark the listing sold (best-effort; product may be missing in odd states).
        var product = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == tx.ProductId, ct);
        if (product is not null && product.Status != ProductStatus.Sold)
        {
            product.Status = ProductStatus.Sold;
            product.UpdatedAtUtc = now;
        }

        // FR-24: audit the buyer's receipt confirmation (deal completed, FR-19).
        _audit.Write(
            action: "Transaction.ReceiptConfirmed",
            entityType: "Transaction",
            entityId: tx.TransactionId.ToString("N"),
            actorUserId: request.BuyerId,
            before: new { Status = from },
            after: new { Status = TransactionStatus.Confirmed.ToString(), tx.ConfirmedAtUtc });

        await _db.SaveChangesAsync(ct);

        // FR-19 -> FR-14: a clean completed deal nudges both parties' trust up.
        // ReasonCodeId 7 = COMPLETED_DEAL (+5) — see DI/migration TODO; until seeded this is a
        // best-effort call that fails soft so receipt confirmation is never blocked.
        await TryRewardCompletedDealAsync(tx, ct);

        return Result<TransactionDto>.Success(ToDto(tx));
    }

    /// <summary>
    /// FR-14: positive trust adjustment on a completed deal for buyer and seller.
    /// TODO(migration): seed BlacklistReasonCode { Code="COMPLETED_DEAL", Severity=1,
    /// DefaultScorePenalty=+5 } as ReasonCodeId 7. Until then this no-ops gracefully.
    /// </summary>
    private async Task TryRewardCompletedDealAsync(Transaction tx, CancellationToken ct)
    {
        const int completedDealReasonCodeId = 7; // TODO: confirm id once seed row is added.
        var reasonExists = await _db.BlacklistReasonCodes
            .AnyAsync(r => r.ReasonCodeId == completedDealReasonCodeId, ct);
        if (!reasonExists)
        {
            _logger.LogDebug("COMPLETED_DEAL reason code not seeded yet; skipping trust reward for tx {TxId}.", tx.TransactionId);
            return;
        }

        foreach (var userId in new[] { tx.BuyerId, tx.SellerId })
        {
            var result = await _trustScore.ApplyEventAsync(new ApplyTrustEventRequest(
                UserId: userId,
                ReasonCodeId: completedDealReasonCodeId,
                RelatedTransactionId: tx.TransactionId,
                DeltaOverride: 5,
                ActorUserId: null,
                Note: "Completed deal (FR-19)."), ct);
            if (!result.Succeeded)
                _logger.LogWarning("Trust reward failed for user {UserId} on tx {TxId}: {Error}",
                    userId, tx.TransactionId, result.Error);
        }
    }

    private static TransactionDto ToDto(Transaction t) => new(
        t.TransactionId, t.ProductId, t.SellerId, t.BuyerId, t.WinningBidId,
        t.AgreedAmount, t.Currency, t.Status.ToString());
}
