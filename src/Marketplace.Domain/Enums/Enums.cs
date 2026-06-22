namespace Marketplace.Domain.Enums;

// All enums map to VARCHAR columns via HasConversion<string>() in EF config,
// mirroring the CHECK constraints in sql/schema.sql. Keep names == DB string values.

/// <summary>User RBAC role. CHECK CK_Users_Role.</summary>
public enum UserRole { Member, Admin, Support }

/// <summary>
/// Account lifecycle. CHECK CK_Users_Status.
/// B-05/G-5: 'PendingBanReview' = auto-flagged for ban, awaiting human Admin confirmation
/// (due-process DP-3: no permanent auto-ban; SRS 6.2 state machine).
/// </summary>
public enum AccountStatus { Active, Suspended, PendingBanReview, Banned }

/// <summary>Membership lifecycle (FR-27). CHECK CK_Membership_Status. Default = Trial.</summary>
public enum MembershipStatus { Trial, Active, Expired, Cancelled }

/// <summary>Listing type. CHECK CK_Products_Type.</summary>
public enum ListingType { FixedPrice, Auction }

/// <summary>Product/listing status. CHECK CK_Products_Status.</summary>
public enum ProductStatus { Draft, Active, Sold, Closed, Removed }

/// <summary>Auction status. CHECK CK_Auctions_Status.</summary>
public enum AuctionStatus { Scheduled, Open, Closed, Cancelled }

/// <summary>Bid status. CHECK CK_Bids_Status.</summary>
public enum BidStatus { Active, Outbid, Won, Retracted, Voided }

/// <summary>No-touch transaction flow (FR-18). CHECK CK_Tx_Status. No Held/Escrow/Refunded by design.</summary>
public enum TransactionStatus { Pending, Transferred, Confirmed, Disputed, Cancelled }

/// <summary>Penalty action type. CHECK CK_Penalty_Type.</summary>
public enum PenaltyActionType { Warning, ScoreDeduct, Suspend, Ban }

/// <summary>Internal blacklist review state (FR-17, due process). CHECK CK_Blacklist_Review.</summary>
public enum BlacklistReviewStatus { PendingReview, Confirmed, Rejected, Appealed, Overturned }

/// <summary>Blacklist appeal state. CHECK CK_Blacklist_Appeal.</summary>
public enum BlacklistAppealStatus { None, Requested, UnderReview, Accepted, Denied }

/// <summary>Dispute status. CHECK CK_Dispute_Status.</summary>
public enum DisputeStatus { Open, UnderReview, Resolved, Rejected, Escalated }

/// <summary>Platform-revenue fee type (company money, separate from buyer/seller). CHECK CK_Fee_Type.</summary>
public enum FeeType { Membership, MembershipRenewal, MembershipUpgrade, Listing, Premium, Featured }

/// <summary>Fee invoice status. CHECK CK_Fee_Status.</summary>
public enum FeeInvoiceStatus { Issued, Paid, Void, Overdue }

/// <summary>Payment-slip review state (Flow B membership payment). CHECK CK_Slip_Status.</summary>
public enum PaymentSlipStatus { Pending, Approved, Rejected }

/// <summary>Single-level referral status (FR-28). CHECK CK_Referral_Status.</summary>
public enum ReferralStatus { Pending, Qualified, Rewarded, Rejected }

/// <summary>
/// Credit ledger entry type (FR-29). CHECK CK_CreditTx_Type.
/// S-04: 'Revoke' = clawback of previously earned credit (e.g. referral abuse confirmed),
/// distinct from generic 'Adjustment' so FR-28/29 revoke is auditable &amp; unambiguous.
/// NOTE: NO Withdraw/CashOut/Transfer by design — credit is non-cashable, non-transferable.
/// </summary>
public enum CreditTransactionType { ReferralReward, PromoSpend, Adjustment, Expiry, Revoke }

/// <summary>Listing promotion type. CHECK CK_PromoPkg_Type / CK_ListPromo_Type.</summary>
public enum PromotionType { Featured, TopOfList, Highlight }

/// <summary>Listing promotion status. CHECK CK_ListPromo_Status.</summary>
public enum ListingPromotionStatus { Active, Expired, Cancelled }

/// <summary>Notification kind (B-01/G-1, FR-27 advance-notice log). CHECK CK_Notif_Type.</summary>
public enum NotificationType
{
    TrialExpiring, RenewalDue, RenewalCharged, MembershipExpired,
    PromotionExpiring, PenaltyIssued, AppealUpdate
}

/// <summary>Notification delivery channel. CHECK CK_Notif_Channel.</summary>
public enum NotificationChannel { Email, InApp, Sms }

/// <summary>Notification delivery status. CHECK CK_Notif_Status.</summary>
public enum NotificationStatus { Pending, Sent, Failed, Skipped }
