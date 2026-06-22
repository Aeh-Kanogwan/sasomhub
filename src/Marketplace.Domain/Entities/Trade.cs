using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Entities;

// ============================ 7. TRANSACTIONS (NO-TOUCH PAYMENT) ==========
// FR-18: platform never holds money. Transaction is a record of an off-platform
// direct transfer only — NO wallet/balance/escrow/held/refunded fields exist anywhere.

/// <summary>dbo.Transactions — AgreedAmount used for reference/fee calc only. ExternalPaymentNote = off-platform transfer note.</summary>
public class Transaction
{
    public Guid TransactionId { get; set; }
    public Guid ProductId { get; set; }
    public Guid SellerId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid? WinningBidId { get; set; }
    public decimal AgreedAmount { get; set; }      // CHECK >= 0
    public string Currency { get; set; } = "THB";
    public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
    public string? ExternalPaymentNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? TransferredAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public Product Product { get; set; } = null!;
    public User Seller { get; set; } = null!;
    public User Buyer { get; set; } = null!;
    public Bid? WinningBid { get; set; }
    public ICollection<TransactionStatusHistory> StatusHistory { get; set; } = new List<TransactionStatusHistory>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<Dispute> Disputes { get; set; } = new List<Dispute>();
}

/// <summary>dbo.TransactionStatusHistory — append log, ON DELETE CASCADE from Transaction.</summary>
public class TransactionStatusHistory
{
    public long TransactionStatusHistoryId { get; set; }
    public Guid TransactionId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = null!;
    public Guid? ChangedByUserId { get; set; }     // null => worker/system
    public string? Note { get; set; }
    public DateTime ChangedAtUtc { get; set; }

    public Transaction Transaction { get; set; } = null!;
    public User? ChangedByUser { get; set; }
}

// ============================ 8. REVIEWS ==================================

/// <summary>dbo.Reviews — one review per (Transaction, Reviewer). Rating 1-5.</summary>
public class Review
{
    public Guid ReviewId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid ReviewerId { get; set; }
    public Guid RevieweeId { get; set; }
    public byte Rating { get; set; }               // CHECK 1-5
    public string? Comment { get; set; }
    public bool IsHidden { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Transaction Transaction { get; set; } = null!;
    public User Reviewer { get; set; } = null!;
    public User Reviewee { get; set; } = null!;
}

// ============================ 11. DISPUTES ================================

/// <summary>dbo.Disputes — platform is mediator, never pays/refunds (no-touch, FR-21).</summary>
public class Dispute
{
    public Guid DisputeId { get; set; }
    public Guid TransactionId { get; set; }
    public Guid RaisedByUserId { get; set; }
    public int? ReasonCodeId { get; set; }
    public string? Description { get; set; }
    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public string? Resolution { get; set; }
    public Guid? HandledByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }

    public Transaction Transaction { get; set; } = null!;
    public User RaisedByUser { get; set; } = null!;
    public BlacklistReasonCode? ReasonCode { get; set; }
    public User? HandledByUser { get; set; }
}
