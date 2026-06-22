using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Entities;

// ============================ 12. FEE INVOICES (company money) =============
// FR-22: platform revenue (membership/renewal/upgrade/listing fees). Fully separated
// from buyer<->seller money. Settled via company gateway (ExternalPaymentRef). No commission %.

/// <summary>dbo.FeeInvoices — company revenue, billed separately from trade flow.</summary>
public class FeeInvoice
{
    public Guid FeeInvoiceId { get; set; }
    public Guid UserId { get; set; }
    public FeeType FeeType { get; set; }
    public Guid? RelatedMembershipId { get; set; }
    public Guid? RelatedProductId { get; set; }
    public decimal Amount { get; set; }            // CHECK >= 0
    public string Currency { get; set; } = "THB";
    public FeeInvoiceStatus Status { get; set; } = FeeInvoiceStatus.Issued;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? ExternalPaymentRef { get; set; }

    public User User { get; set; } = null!;
    public Membership? RelatedMembership { get; set; }
    public Product? RelatedProduct { get; set; }
}

// ============================ 12b. PAYMENT SLIPS (Flow B) ==================
// FR-22/FR-35: "Flow B" membership payment = bank transfer into the company account, the member
// uploads the transfer slip, an Admin confirms it in the dashboard. The settled FeeInvoice then
// drives the actual membership renewal/upgrade. This is COMPANY money (membership fee) — fully
// separate from buyer↔seller trade money, so receiving/confirming it does NOT touch the no-touch
// trade flow. There is still NO wallet/escrow for trade money anywhere.

/// <summary>
/// dbo.PaymentSlips — a member-submitted proof of an off-platform bank transfer that pays a company
/// <see cref="FeeInvoice"/> (membership fee). An Admin reviews and Approves/Rejects it; approval marks
/// the invoice Paid and triggers the membership renewal/upgrade (Flow B).
/// </summary>
public class PaymentSlip
{
    public Guid PaymentSlipId { get; set; }
    public Guid FeeInvoiceId { get; set; }
    public Guid UserId { get; set; }                 // submitter (must own the invoice)
    public string SlipImageUrl { get; set; } = null!;
    public decimal AmountClaimed { get; set; }       // CHECK >= 0 — amount the member says they transferred
    public DateTime TransferredAtUtc { get; set; }   // date/time on the slip
    public string? BankRefNote { get; set; }
    public PaymentSlipStatus Status { get; set; } = PaymentSlipStatus.Pending;
    public DateTime SubmittedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }      // admin who reviewed (null until reviewed)
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public FeeInvoice FeeInvoice { get; set; } = null!;
    public User User { get; set; } = null!;
    public User? ReviewedByUser { get; set; }
}

// ============================ 13. REFERRAL (SINGLE-LEVEL ONLY) =============
// Legal: single-level by design. No upline/downline/level. Rewards paid in
// non-cashable platform credit only — never cash (avoids MLM/pyramid, FR-28).

/// <summary>dbo.ReferralCodes — one active code per user.</summary>
public class ReferralCode
{
    public Guid ReferralCodeId { get; set; }
    public Guid UserId { get; set; }
    public string Code { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
}

/// <summary>
/// dbo.Referrals — flat single-level. A user can be REFERRED only once (UQ_Referral_Referred).
/// NOTE: intentionally NO ParentReferralId / UplineUserId / Level column (Legal #9, FR-28).
/// </summary>
public class Referral
{
    public Guid ReferralId { get; set; }
    public Guid ReferrerUserId { get; set; }
    public Guid ReferredUserId { get; set; }
    public string ReferralCode { get; set; } = null!;
    public ReferralStatus Status { get; set; } = ReferralStatus.Pending;
    public decimal RewardCreditToReferrer { get; set; }   // credit, not cash
    public decimal RewardCreditToReferred { get; set; }   // credit, not cash
    public DateTime? RewardedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User Referrer { get; set; } = null!;
    public User Referred { get; set; } = null!;
}

// ============================ 14. PLATFORM CREDIT (NON-CASHABLE) ===========
// Legal: non-cashable, non-transferable loyalty point; spendable on platform services
// only; can expire. Outside พ.ร.บ.ระบบการชำระเงิน 2560. NO withdraw/transfer path (FR-28/29).

/// <summary>dbo.CreditAccounts — 1:1 with User, CASCADE. Balance maintained from ledger, CHECK >= 0. NOT a money wallet.</summary>
public class CreditAccount
{
    public Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public User User { get; set; } = null!;
}

/// <summary>
/// dbo.CreditTransactions — append-only ledger (never UPDATE/DELETE).
/// Amount: positive = earn, negative = spend/expire. NO Withdraw/CashOut/Transfer type by design.
/// </summary>
public class CreditTransaction
{
    public long CreditTransactionId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public CreditTransactionType Type { get; set; }
    public string? RefId { get; set; }
    /// <summary>
    /// B-02/G-2: idempotency guard against double-credit on retry/double-click/worker re-run.
    /// App supplies a stable key per logical credit op (e.g. "Referral:{ReferralId}",
    /// "PromoSpend:{ListingPromotionId}"). Unique when supplied (UX_CreditTx_Idempotency).
    /// </summary>
    public string? IdempotencyKey { get; set; }
    public decimal BalanceAfter { get; set; }      // CHECK >= 0
    public DateTime? ExpiresAtUtc { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
}

// ============================ 15. FEATURED / PROMOTED LISTINGS ============

/// <summary>dbo.ListingPromotions — credit-paid promotion; links to the PromoSpend ledger row (FR-30).</summary>
public class ListingPromotion
{
    public Guid ListingPromotionId { get; set; }
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }               // buyer of promotion (usually seller)
    public byte? PromotionPackageId { get; set; }
    public PromotionType PromotionType { get; set; }
    public decimal CreditCost { get; set; }        // CHECK >= 0
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }        // CHECK > StartsAtUtc
    public ListingPromotionStatus Status { get; set; } = ListingPromotionStatus.Active;
    /// <summary>
    /// Y-08: PromoSpend ledger row that debited the credit. NOT NULL + UNIQUE — a promotion is
    /// only created AFTER credit is debited, and each debit backs exactly one promotion (pairs with B-02).
    /// </summary>
    public long CreditTransactionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Product Product { get; set; } = null!;
    public User User { get; set; } = null!;
    public PromotionPackage? PromotionPackage { get; set; }
    public CreditTransaction CreditTransaction { get; set; } = null!;
}
