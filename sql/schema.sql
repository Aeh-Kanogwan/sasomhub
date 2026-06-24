/* =============================================================================
   Marketplace Collectibles - SQL Server Schema (MVP)
   Stack: .NET 8 Web API + EF Core (Code-First) + SQL Server + BackgroundService
   Author: lead-dev   Date: 2026-06-17

   LEGAL-DRIVEN DESIGN (details in docs/DB_Schema.md):
     1. No-touch payment : no wallet/balance/ledger. Transactions = record of
                           direct off-platform transfer only.
     2. e-KYC/NDID       : no raw ID card / bankbook images. Store status +
                           provider ref + timestamp only.
     3. Internal blacklist : standard reason codes (FK), ReviewStatus/Appeal/ExpiresAt.
     4. Audit/PDPA       : AuditLogs + ConsentRecords + soft-delete/anonymize.
     5. Anti-shill       : Bids store IP hash / device fingerprint hash / relationship flag.
     6. Out of MVP       : Pre-Order / Escrow / Wallet (later phase).

   Encryption: columns marked [ENCRYPTED] are intended for Always Encrypted at real
   deploy. DDL below uses plain types so it runs immediately; bind CMK/CEK separately.
   ========================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ============================ 1. LOOKUP / ENUM TABLES ===================== */

CREATE TABLE dbo.KycStatuses (
    KycStatusId     TINYINT       NOT NULL PRIMARY KEY,
    Code            VARCHAR(20)   NOT NULL,
    DisplayName     NVARCHAR(50)  NOT NULL,
    CONSTRAINT UQ_KycStatuses_Code UNIQUE (Code)
);
GO

CREATE TABLE dbo.BlacklistReasonCodes (
    ReasonCodeId    INT           NOT NULL PRIMARY KEY,
    Code            VARCHAR(40)   NOT NULL,
    DisplayName     NVARCHAR(120) NOT NULL,
    Severity        TINYINT       NOT NULL,
    DefaultScorePenalty INT       NOT NULL CONSTRAINT DF_Reason_Penalty DEFAULT (0),
    IsActive        BIT           NOT NULL CONSTRAINT DF_Reason_Active DEFAULT (1),
    CONSTRAINT UQ_ReasonCodes_Code UNIQUE (Code),
    CONSTRAINT CK_ReasonCodes_Severity CHECK (Severity BETWEEN 1 AND 5)
);
GO

CREATE TABLE dbo.Categories (
    CategoryId      INT           IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ParentCategoryId INT          NULL,
    Name            NVARCHAR(120) NOT NULL,
    Slug            VARCHAR(140)  NOT NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_Categories_Active DEFAULT (1),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Categories_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_Categories_Slug UNIQUE (Slug),
    CONSTRAINT FK_Categories_Parent FOREIGN KEY (ParentCategoryId) REFERENCES dbo.Categories (CategoryId)
);
GO

/* ============================ 2. USERS & PROFILE ========================== */

CREATE TABLE dbo.Users (
    UserId          UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Users_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    Email           NVARCHAR(256) NOT NULL,
    NormalizedEmail NVARCHAR(256) NOT NULL,
    PasswordHash    NVARCHAR(512) NULL,
    PhoneNumber     NVARCHAR(32)  NULL,
    Role            VARCHAR(20)   NOT NULL CONSTRAINT DF_Users_Role DEFAULT ('Member'),
    AccountStatus   VARCHAR(20)   NOT NULL CONSTRAINT DF_Users_Status DEFAULT ('Active'),
    EmailConfirmed  BIT           NOT NULL CONSTRAINT DF_Users_EmailConf DEFAULT (0),
    IsDeleted       BIT           NOT NULL CONSTRAINT DF_Users_IsDeleted DEFAULT (0),
    IsAnonymized    BIT           NOT NULL CONSTRAINT DF_Users_IsAnon DEFAULT (0),
    DeletedAtUtc    DATETIME2(3)  NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Users_Created DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Users_Updated DEFAULT (SYSUTCDATETIME()),
    RowVersion      ROWVERSION,
    CONSTRAINT CK_Users_Role CHECK (Role IN ('Member','Admin','Support')),
    -- B-05/G-5: 'PendingBanReview' = auto-flagged for ban, awaiting human Admin confirmation
    --           (due-process DP-3: no permanent auto-ban). Maps SRS 6.2 state machine.
    CONSTRAINT CK_Users_Status CHECK (AccountStatus IN ('Active','Suspended','PendingBanReview','Banned'))
);
GO
CREATE UNIQUE INDEX UX_Users_NormalizedEmail ON dbo.Users (NormalizedEmail) WHERE IsDeleted = 0;
GO

CREATE TABLE dbo.UserProfiles (
    UserId          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    DisplayName     NVARCHAR(80)  NOT NULL,
    AvatarUrl       NVARCHAR(512) NULL,
    Bio             NVARCHAR(1000) NULL,
    ProvinceCode    VARCHAR(10)   NULL,
    UpdatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_UserProfiles_Updated DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_UserProfiles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE
);
GO

/* ============================ 3. KYC (e-KYC / NDID) ======================= */
CREATE TABLE dbo.KycVerifications (
    KycVerificationId UNIQUEIDENTIFIER NOT NULL
                      CONSTRAINT DF_Kyc_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    KycStatusId     TINYINT       NOT NULL,
    Provider        VARCHAR(40)   NOT NULL,
    ProviderReference VARCHAR(128) NULL,
    VerificationLevel TINYINT     NOT NULL CONSTRAINT DF_Kyc_Level DEFAULT (0),
    VerifiedAtUtc   DATETIME2(3)  NULL,
    ExpiresAtUtc    DATETIME2(3)  NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Kyc_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Kyc_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Kyc_Status FOREIGN KEY (KycStatusId) REFERENCES dbo.KycStatuses (KycStatusId)
);
GO
CREATE INDEX IX_Kyc_UserId ON dbo.KycVerifications (UserId);
GO

CREATE TABLE dbo.KycSensitiveData (
    KycVerificationId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    FullNameMasked  NVARCHAR(256) NULL,
    NationalIdHash  VARBINARY(64) NULL,
    RetentionExpiresAtUtc DATETIME2(3) NOT NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_KycSens_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_KycSens_Kyc FOREIGN KEY (KycVerificationId)
        REFERENCES dbo.KycVerifications (KycVerificationId) ON DELETE CASCADE
);
GO

/* ============================ 4. MEMBERSHIP / SUBSCRIPTION ================ */
CREATE TABLE dbo.MembershipTiers (
    MembershipTierId TINYINT      NOT NULL PRIMARY KEY,
    Code            VARCHAR(20)   NOT NULL,
    DisplayName     NVARCHAR(50)  NOT NULL,
    RequiresKyc     BIT           NOT NULL CONSTRAINT DF_Tier_Kyc DEFAULT (0),
    MonthlyFee      DECIMAL(10,2) NOT NULL CONSTRAINT DF_Tier_Fee DEFAULT (0),
    AnnualPriceTHB  DECIMAL(10,2) NOT NULL CONSTRAINT DF_Tier_Annual DEFAULT (0),  -- annual membership price (THB); Normal=1000 / Verified=1500 / Premium=2000
    IsActive        BIT           NOT NULL CONSTRAINT DF_Tier_Active DEFAULT (1),
    CONSTRAINT UQ_Tiers_Code UNIQUE (Code),
    CONSTRAINT CK_Tier_AnnualPrice CHECK (AnnualPriceTHB >= 0)
);
GO

CREATE TABLE dbo.Memberships (
    MembershipId    UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Membership_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    MembershipTierId TINYINT      NOT NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Membership_Status DEFAULT ('Trial'),
    StartAtUtc      DATETIME2(3)  NOT NULL CONSTRAINT DF_Membership_Start DEFAULT (SYSUTCDATETIME()),
    EndAtUtc        DATETIME2(3)  NULL,
    -- 3-month free trial: TrialEndsAtUtc = TrialStartsAtUtc + 3 months (set by app/worker)
    TrialStartsAtUtc DATETIME2(3) NULL,
    TrialEndsAtUtc  DATETIME2(3)  NULL,
    -- annual paid membership expiry; worker flips Status->Expired after this instant
    PaidThroughUtc  DATETIME2(3)  NULL,
    AutoRenew       BIT           NOT NULL CONSTRAINT DF_Membership_AutoRenew DEFAULT (0),
    -- Y-06/B-04: price snapshot at the moment this membership cycle was paid. Locks the
    -- charged amount so later config price changes (ConfigVersions) never apply retroactively
    -- to an already-paid cycle (FR-31 "no retroactive pricing"). NULL while still in Trial.
    PaidAmountTHB   DECIMAL(10,2) NULL,
    -- Flow B: target tier of a requested-but-not-yet-paid upgrade. Set when the upgrade FeeInvoice is
    -- issued; the tier only switches once the fee is confirmed paid (Admin approves the slip), then cleared.
    PendingUpgradeTierId TINYINT  NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Membership_Created DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Membership_Updated DEFAULT (SYSUTCDATETIME()),
    RowVersion      ROWVERSION,
    CONSTRAINT FK_Membership_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Membership_Tier FOREIGN KEY (MembershipTierId) REFERENCES dbo.MembershipTiers (MembershipTierId),
    CONSTRAINT CK_Membership_Status CHECK (Status IN ('Trial','Active','Expired','Cancelled')),
    CONSTRAINT CK_Membership_Trial CHECK (TrialEndsAtUtc IS NULL OR TrialStartsAtUtc IS NULL OR TrialEndsAtUtc > TrialStartsAtUtc),
    -- Y-05: a Trial membership MUST have an expiry instant (no never-ending free trial;
    --       FR-27 / Legal #8 consumer protection).
    CONSTRAINT CK_Membership_TrialHasEnd CHECK (Status <> 'Trial' OR TrialEndsAtUtc IS NOT NULL),
    CONSTRAINT CK_Membership_PaidAmount CHECK (PaidAmountTHB IS NULL OR PaidAmountTHB >= 0)
);
GO
-- One live membership per user: at most one Trial OR Active row per user at a time.
CREATE UNIQUE INDEX UX_Membership_LivePerUser ON dbo.Memberships (UserId) WHERE Status IN ('Trial','Active');
GO
-- S-03: lets the BackgroundService scan expiring/expired memberships fast for
-- expiry flips and the 14/3/1-day notification sweep (FR-27).
CREATE INDEX IX_Membership_Expiry ON dbo.Memberships (Status, PaidThroughUtc, TrialEndsAtUtc);
GO

/* ============================ 5. PRODUCTS / LISTINGS ====================== */
CREATE TABLE dbo.Products (
    ProductId       UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Products_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    SellerId        UNIQUEIDENTIFIER NOT NULL,
    CategoryId      INT           NOT NULL,
    Title           NVARCHAR(200) NOT NULL,
    Description     NVARCHAR(MAX) NULL,
    ConditionGrade  VARCHAR(20)   NOT NULL CONSTRAINT DF_Products_Cond DEFAULT ('Used'),
    Rarity          VARCHAR(20)   NOT NULL CONSTRAINT DF_Products_Rarity DEFAULT ('common'),  -- card rarity slug; drives card border + Explore rarity facet (FR-08)
    SerialLabel     NVARCHAR(40)  NULL,         -- display-only serial e.g. '#0042/0050' (not a guarantee, FR-07)
    ListingType     VARCHAR(20)   NOT NULL CONSTRAINT DF_Products_Type DEFAULT ('FixedPrice'),
    FixedPrice      DECIMAL(12,2) NULL,
    Currency        CHAR(3)       NOT NULL CONSTRAINT DF_Products_Cur DEFAULT ('THB'),
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Products_Status DEFAULT ('Draft'),
    IsDeleted       BIT           NOT NULL CONSTRAINT DF_Products_Del DEFAULT (0),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Products_Created DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Products_Updated DEFAULT (SYSUTCDATETIME()),
    RowVersion      ROWVERSION,
    CONSTRAINT FK_Products_Seller FOREIGN KEY (SellerId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Products_Category FOREIGN KEY (CategoryId) REFERENCES dbo.Categories (CategoryId),
    CONSTRAINT CK_Products_Type CHECK (ListingType IN ('FixedPrice','Auction')),
    CONSTRAINT CK_Products_Status CHECK (Status IN ('Draft','Active','Sold','Closed','Removed')),
    CONSTRAINT CK_Products_FixedPrice CHECK (FixedPrice IS NULL OR FixedPrice >= 0),
    CONSTRAINT CK_Products_Rarity CHECK (Rarity IN ('common','rare','epic','legendary'))
);
GO
CREATE INDEX IX_Products_Seller ON dbo.Products (SellerId) WHERE IsDeleted = 0;
CREATE INDEX IX_Products_Category_Status ON dbo.Products (CategoryId, Status) WHERE IsDeleted = 0;
-- FR-08: Explore rarity facet over Active listings.
CREATE INDEX IX_Products_Rarity_Status ON dbo.Products (Rarity, Status) WHERE IsDeleted = 0;
GO

CREATE TABLE dbo.ProductImages (
    ProductImageId  UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_ProductImages_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    Url             NVARCHAR(512) NOT NULL,
    SortOrder       INT           NOT NULL CONSTRAINT DF_ProductImages_Sort DEFAULT (0),
    IsPrimary       BIT           NOT NULL CONSTRAINT DF_ProductImages_Primary DEFAULT (0),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_ProductImages_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_ProductImages_Product FOREIGN KEY (ProductId)
        REFERENCES dbo.Products (ProductId) ON DELETE CASCADE
);
GO
CREATE INDEX IX_ProductImages_Product ON dbo.ProductImages (ProductId);
GO

/* ============================ 6. AUCTIONS / BIDS ========================== */
CREATE TABLE dbo.Auctions (
    AuctionId       UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Auctions_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    StartingPrice   DECIMAL(12,2) NOT NULL,
    ReservePrice    DECIMAL(12,2) NULL,
    BidIncrement    DECIMAL(12,2) NOT NULL CONSTRAINT DF_Auctions_Inc DEFAULT (1),
    CurrentHighBid  DECIMAL(12,2) NULL,
    WinningBidId    UNIQUEIDENTIFIER NULL,
    StartAtUtc      DATETIME2(3)  NOT NULL,
    EndAtUtc        DATETIME2(3)  NOT NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Auctions_Status DEFAULT ('Scheduled'),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Auctions_Created DEFAULT (SYSUTCDATETIME()),
    RowVersion      ROWVERSION,
    CONSTRAINT FK_Auctions_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId),
    CONSTRAINT CK_Auctions_Status CHECK (Status IN ('Scheduled','Open','Closed','Cancelled')),
    CONSTRAINT CK_Auctions_Time CHECK (EndAtUtc > StartAtUtc),
    CONSTRAINT CK_Auctions_Prices CHECK (StartingPrice >= 0 AND (ReservePrice IS NULL OR ReservePrice >= StartingPrice))
);
GO
CREATE UNIQUE INDEX UX_Auctions_Product ON dbo.Auctions (ProductId);
CREATE INDEX IX_Auctions_Status_End ON dbo.Auctions (Status, EndAtUtc);
GO

CREATE TABLE dbo.Bids (
    BidId           UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Bids_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    AuctionId       UNIQUEIDENTIFIER NOT NULL,
    BidderId        UNIQUEIDENTIFIER NOT NULL,
    Amount          DECIMAL(12,2) NOT NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Bids_Status DEFAULT ('Active'),
    IpAddressHash   VARBINARY(32) NULL,
    DeviceFingerprintHash VARBINARY(32) NULL,
    IsFlaggedShill  BIT           NOT NULL CONSTRAINT DF_Bids_Shill DEFAULT (0),
    RelationshipFlag VARCHAR(30)  NULL,
    PlacedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Bids_Placed DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Bids_Auction FOREIGN KEY (AuctionId) REFERENCES dbo.Auctions (AuctionId),
    CONSTRAINT FK_Bids_Bidder FOREIGN KEY (BidderId) REFERENCES dbo.Users (UserId),
    CONSTRAINT CK_Bids_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Bids_Status CHECK (Status IN ('Active','Outbid','Won','Retracted','Voided'))
);
GO
CREATE INDEX IX_Bids_Auction_Amount ON dbo.Bids (AuctionId, Amount DESC);
CREATE INDEX IX_Bids_Bidder ON dbo.Bids (BidderId);
CREATE INDEX IX_Bids_Shill ON dbo.Bids (AuctionId) WHERE IsFlaggedShill = 1;
GO
ALTER TABLE dbo.Auctions
    ADD CONSTRAINT FK_Auctions_WinningBid FOREIGN KEY (WinningBidId) REFERENCES dbo.Bids (BidId);
GO

/* ============================ 7. TRANSACTIONS (NO-TOUCH PAYMENT) ========== */
CREATE TABLE dbo.Transactions (
    TransactionId   UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Tx_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    SellerId        UNIQUEIDENTIFIER NOT NULL,
    BuyerId         UNIQUEIDENTIFIER NOT NULL,
    WinningBidId    UNIQUEIDENTIFIER NULL,
    AgreedAmount    DECIMAL(12,2) NOT NULL,
    Currency        CHAR(3)       NOT NULL CONSTRAINT DF_Tx_Cur DEFAULT ('THB'),
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Tx_Status DEFAULT ('Pending'),
    ExternalPaymentNote NVARCHAR(400) NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Tx_Created DEFAULT (SYSUTCDATETIME()),
    TransferredAtUtc DATETIME2(3) NULL,
    ConfirmedAtUtc  DATETIME2(3)  NULL,
    RowVersion      ROWVERSION,
    CONSTRAINT FK_Tx_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId),
    CONSTRAINT FK_Tx_Seller FOREIGN KEY (SellerId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Tx_Buyer FOREIGN KEY (BuyerId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Tx_WinningBid FOREIGN KEY (WinningBidId) REFERENCES dbo.Bids (BidId),
    CONSTRAINT CK_Tx_Status CHECK (Status IN ('Pending','Transferred','Confirmed','Disputed','Cancelled')),
    CONSTRAINT CK_Tx_Amount CHECK (AgreedAmount >= 0),
    CONSTRAINT CK_Tx_Parties CHECK (BuyerId <> SellerId)
);
GO
CREATE INDEX IX_Tx_Seller ON dbo.Transactions (SellerId);
CREATE INDEX IX_Tx_Buyer ON dbo.Transactions (BuyerId);
CREATE INDEX IX_Tx_Status ON dbo.Transactions (Status);
GO

CREATE TABLE dbo.TransactionStatusHistory (
    TransactionStatusHistoryId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TransactionId   UNIQUEIDENTIFIER NOT NULL,
    FromStatus      VARCHAR(20)   NULL,
    ToStatus        VARCHAR(20)   NOT NULL,
    ChangedByUserId UNIQUEIDENTIFIER NULL,
    Note            NVARCHAR(400) NULL,
    ChangedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_TxHist_Changed DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_TxHist_Tx FOREIGN KEY (TransactionId)
        REFERENCES dbo.Transactions (TransactionId) ON DELETE CASCADE,
    CONSTRAINT FK_TxHist_User FOREIGN KEY (ChangedByUserId) REFERENCES dbo.Users (UserId)
);
GO
CREATE INDEX IX_TxHist_Tx ON dbo.TransactionStatusHistory (TransactionId, ChangedAtUtc);
GO

/* ============================ 8. REVIEWS ================================== */
CREATE TABLE dbo.Reviews (
    ReviewId        UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Reviews_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    TransactionId   UNIQUEIDENTIFIER NOT NULL,
    ReviewerId      UNIQUEIDENTIFIER NOT NULL,
    RevieweeId      UNIQUEIDENTIFIER NOT NULL,
    Rating          TINYINT       NOT NULL,
    Comment         NVARCHAR(1000) NULL,
    IsHidden        BIT           NOT NULL CONSTRAINT DF_Reviews_Hidden DEFAULT (0),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Reviews_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Reviews_Tx FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions (TransactionId),
    CONSTRAINT FK_Reviews_Reviewer FOREIGN KEY (ReviewerId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Reviews_Reviewee FOREIGN KEY (RevieweeId) REFERENCES dbo.Users (UserId),
    CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
    CONSTRAINT CK_Reviews_Parties CHECK (ReviewerId <> RevieweeId),
    CONSTRAINT UQ_Reviews_OncePerTx UNIQUE (TransactionId, ReviewerId)
);
GO
CREATE INDEX IX_Reviews_Reviewee ON dbo.Reviews (RevieweeId) WHERE IsHidden = 0;
GO

/* ============================ 9. TRUST SCORE ============================== */
CREATE TABLE dbo.TrustScores (
    UserId          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Score           INT           NOT NULL CONSTRAINT DF_TrustScore_Score DEFAULT (100),
    LastCalculatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_TrustScore_Calc DEFAULT (SYSUTCDATETIME()),
    RowVersion      ROWVERSION,
    CONSTRAINT FK_TrustScores_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE,
    CONSTRAINT CK_TrustScore_Range CHECK (Score BETWEEN 0 AND 100)
);
GO

CREATE TABLE dbo.TrustScoreHistory (
    TrustScoreHistoryId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    Delta           INT           NOT NULL,
    ScoreAfter      INT           NOT NULL,
    ReasonCodeId    INT           NULL,
    RelatedTransactionId UNIQUEIDENTIFIER NULL,
    Note            NVARCHAR(400) NULL,
    CreatedByUserId UNIQUEIDENTIFIER NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_TSHist_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_TSHist_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_TSHist_Reason FOREIGN KEY (ReasonCodeId) REFERENCES dbo.BlacklistReasonCodes (ReasonCodeId),
    CONSTRAINT FK_TSHist_Tx FOREIGN KEY (RelatedTransactionId) REFERENCES dbo.Transactions (TransactionId)
);
GO
CREATE INDEX IX_TSHist_User ON dbo.TrustScoreHistory (UserId, CreatedAtUtc);
GO

/* ============================ 10. PENALTY / BLACKLIST ===================== */
CREATE TABLE dbo.PenaltyActions (
    PenaltyActionId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Penalty_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    ActionType      VARCHAR(20)   NOT NULL,
    ReasonCodeId    INT           NOT NULL,
    RelatedTransactionId UNIQUEIDENTIFIER NULL,
    IssuedByUserId  UNIQUEIDENTIFIER NULL,
    EffectiveAtUtc  DATETIME2(3)  NOT NULL CONSTRAINT DF_Penalty_Eff DEFAULT (SYSUTCDATETIME()),
    ExpiresAtUtc    DATETIME2(3)  NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Penalty_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Penalty_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Penalty_Reason FOREIGN KEY (ReasonCodeId) REFERENCES dbo.BlacklistReasonCodes (ReasonCodeId),
    CONSTRAINT FK_Penalty_Tx FOREIGN KEY (RelatedTransactionId) REFERENCES dbo.Transactions (TransactionId),
    CONSTRAINT CK_Penalty_Type CHECK (ActionType IN ('Warning','ScoreDeduct','Suspend','Ban'))
);
GO
CREATE INDEX IX_Penalty_User ON dbo.PenaltyActions (UserId);
GO

CREATE TABLE dbo.BlacklistEntries (
    BlacklistEntryId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Blacklist_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    ReasonCodeId    INT           NOT NULL,
    InternalNote    NVARCHAR(500) NULL,
    ReviewStatus    VARCHAR(20)   NOT NULL CONSTRAINT DF_Blacklist_Review DEFAULT ('PendingReview'),
    AppealStatus    VARCHAR(20)   NOT NULL CONSTRAINT DF_Blacklist_Appeal DEFAULT ('None'),
    AppealNote      NVARCHAR(500) NULL,
    CreatedByUserId UNIQUEIDENTIFIER NULL,
    ReviewedByUserId UNIQUEIDENTIFIER NULL,
    EffectiveAtUtc  DATETIME2(3)  NOT NULL CONSTRAINT DF_Blacklist_Eff DEFAULT (SYSUTCDATETIME()),
    ExpiresAtUtc    DATETIME2(3)  NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_Blacklist_Active DEFAULT (1),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Blacklist_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Blacklist_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Blacklist_Reason FOREIGN KEY (ReasonCodeId) REFERENCES dbo.BlacklistReasonCodes (ReasonCodeId),
    CONSTRAINT FK_Blacklist_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Blacklist_ReviewedBy FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT CK_Blacklist_Review CHECK (ReviewStatus IN ('PendingReview','Confirmed','Rejected','Appealed','Overturned')),
    CONSTRAINT CK_Blacklist_Appeal CHECK (AppealStatus IN ('None','Requested','UnderReview','Accepted','Denied'))
);
GO
CREATE INDEX IX_Blacklist_User ON dbo.BlacklistEntries (UserId) WHERE IsActive = 1;
GO

/* ============================ 11. DISPUTES ================================ */
CREATE TABLE dbo.Disputes (
    DisputeId       UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Dispute_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    TransactionId   UNIQUEIDENTIFIER NOT NULL,
    RaisedByUserId  UNIQUEIDENTIFIER NOT NULL,
    ReasonCodeId    INT           NULL,
    Description     NVARCHAR(2000) NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Dispute_Status DEFAULT ('Open'),
    Resolution      NVARCHAR(2000) NULL,
    HandledByUserId UNIQUEIDENTIFIER NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Dispute_Created DEFAULT (SYSUTCDATETIME()),
    ResolvedAtUtc   DATETIME2(3)  NULL,
    CONSTRAINT FK_Dispute_Tx FOREIGN KEY (TransactionId) REFERENCES dbo.Transactions (TransactionId),
    CONSTRAINT FK_Dispute_RaisedBy FOREIGN KEY (RaisedByUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Dispute_Reason FOREIGN KEY (ReasonCodeId) REFERENCES dbo.BlacklistReasonCodes (ReasonCodeId),
    CONSTRAINT FK_Dispute_HandledBy FOREIGN KEY (HandledByUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT CK_Dispute_Status CHECK (Status IN ('Open','UnderReview','Resolved','Rejected','Escalated'))
);
GO
CREATE INDEX IX_Dispute_Tx ON dbo.Disputes (TransactionId);
CREATE INDEX IX_Dispute_Status ON dbo.Disputes (Status);
GO

/* ============================ 12. FEE INVOICES (company money) ============ */
CREATE TABLE dbo.FeeInvoices (
    FeeInvoiceId    UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Fee_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    FeeType         VARCHAR(20)   NOT NULL,
    RelatedMembershipId UNIQUEIDENTIFIER NULL,
    RelatedProductId UNIQUEIDENTIFIER NULL,
    Amount          DECIMAL(10,2) NOT NULL,
    Currency        CHAR(3)       NOT NULL CONSTRAINT DF_Fee_Cur DEFAULT ('THB'),
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Fee_Status DEFAULT ('Issued'),
    IssuedAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_Fee_Issued DEFAULT (SYSUTCDATETIME()),
    DueAtUtc        DATETIME2(3)  NULL,
    PaidAtUtc       DATETIME2(3)  NULL,
    ExternalPaymentRef VARCHAR(128) NULL,
    CONSTRAINT FK_Fee_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Fee_Membership FOREIGN KEY (RelatedMembershipId) REFERENCES dbo.Memberships (MembershipId),
    CONSTRAINT FK_Fee_Product FOREIGN KEY (RelatedProductId) REFERENCES dbo.Products (ProductId),
    -- Membership            = annual membership fee (new signup)
    -- MembershipRenewal      = annual auto/manual renewal
    -- MembershipUpgrade      = tier upgrade top-up (e.g. Normal->Premium)
    -- Listing/Premium/Featured = listing-related platform fees
    -- All of the above are PLATFORM REVENUE (company money), billed via FeeInvoices and
    -- settled through the company payment gateway (ExternalPaymentRef). They are fully
    -- separated from buyer<->seller money (no wallet/balance/escrow exists anywhere).
    CONSTRAINT CK_Fee_Type CHECK (FeeType IN ('Membership','MembershipRenewal','MembershipUpgrade','Listing','Premium','Featured')),
    CONSTRAINT CK_Fee_Status CHECK (Status IN ('Issued','Paid','Void','Overdue')),
    CONSTRAINT CK_Fee_Amount CHECK (Amount >= 0)
);
GO
CREATE INDEX IX_Fee_User_Status ON dbo.FeeInvoices (UserId, Status);
GO

/* ============================ 12b. PAYMENT SLIPS (Flow B) ================
   Flow B membership payment: the member transfers the membership fee into the
   company bank account, uploads the transfer slip here, and an Admin confirms it
   in the dashboard. On approval the linked FeeInvoice is marked Paid and the
   membership renewal/upgrade is applied. This is COMPANY money (membership fee),
   fully separate from buyer<->seller trade money — confirming it does NOT touch
   the no-touch trade flow (there is still no wallet/escrow for trade money).
   ======================================================================== */
CREATE TABLE dbo.PaymentSlips (
    PaymentSlipId   UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Slip_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    FeeInvoiceId    UNIQUEIDENTIFIER NOT NULL,
    UserId          UNIQUEIDENTIFIER NOT NULL,   -- submitter (must own the invoice)
    SlipImageUrl    NVARCHAR(512) NOT NULL,
    AmountClaimed   DECIMAL(10,2) NOT NULL,       -- amount the member says they transferred
    TransferredAtUtc DATETIME2(3) NOT NULL,       -- date/time on the slip
    BankRefNote     NVARCHAR(200) NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Slip_Status DEFAULT ('Pending'),
    SubmittedAtUtc  DATETIME2(3)  NOT NULL CONSTRAINT DF_Slip_Submitted DEFAULT (SYSUTCDATETIME()),
    ReviewedByUserId UNIQUEIDENTIFIER NULL,       -- admin who reviewed
    ReviewedAtUtc   DATETIME2(3)  NULL,
    ReviewNote      NVARCHAR(400) NULL,
    RowVersion      ROWVERSION,
    CONSTRAINT FK_Slip_Invoice FOREIGN KEY (FeeInvoiceId) REFERENCES dbo.FeeInvoices (FeeInvoiceId),
    CONSTRAINT FK_Slip_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Slip_ReviewedBy FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT CK_Slip_Status CHECK (Status IN ('Pending','Approved','Rejected')),
    CONSTRAINT CK_Slip_Amount CHECK (AmountClaimed >= 0)
);
GO
-- Admin queue scan: pending slips oldest-first.
CREATE INDEX IX_Slip_Status_Submitted ON dbo.PaymentSlips (Status, SubmittedAtUtc);
GO
CREATE INDEX IX_Slip_Invoice ON dbo.PaymentSlips (FeeInvoiceId);
GO

/* ============================ 13. REFERRAL (SINGLE-LEVEL ONLY) ============
   LEGAL: This program is deliberately SINGLE-LEVEL. A referrer earns a reward
   ONLY for users they personally invited. There is NO upline/downline chain,
   NO multi-tier / binary / matrix structure, and rewards never propagate up
   multiple levels. This avoids being characterised as direct-sales /
   multi-level-marketing or an illegal pyramid/chain scheme
   (พ.ร.บ.ขายตรงและตลาดแบบตรง / กันแชร์ลูกโซ่). Rewards are paid in non-cashable
   platform credit only (see section 14), never in cash.
   ========================================================================== */
CREATE TABLE dbo.ReferralCodes (
    ReferralCodeId  UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_RefCode_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    Code            VARCHAR(20)   NOT NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_RefCode_Active DEFAULT (1),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_RefCode_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_RefCode_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT UQ_RefCode_User UNIQUE (UserId),   -- one code per user
    CONSTRAINT UQ_RefCode_Code UNIQUE (Code)
);
GO

CREATE TABLE dbo.Referrals (
    ReferralId      UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Referral_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    ReferrerUserId  UNIQUEIDENTIFIER NOT NULL,
    ReferredUserId  UNIQUEIDENTIFIER NOT NULL,
    ReferralCode    VARCHAR(20)   NOT NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Referral_Status DEFAULT ('Pending'),
    RewardCreditToReferrer  DECIMAL(10,2) NOT NULL CONSTRAINT DF_Referral_RewReferrer DEFAULT (0),
    RewardCreditToReferred  DECIMAL(10,2) NOT NULL CONSTRAINT DF_Referral_RewReferred DEFAULT (0),
    RewardedAtUtc   DATETIME2(3)  NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Referral_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Referral_Referrer FOREIGN KEY (ReferrerUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Referral_Referred FOREIGN KEY (ReferredUserId) REFERENCES dbo.Users (UserId),
    -- a user can be REFERRED only once (prevents being claimed by multiple referrers)
    CONSTRAINT UQ_Referral_Referred UNIQUE (ReferredUserId),
    -- no self-referral
    CONSTRAINT CK_Referral_NotSelf CHECK (ReferrerUserId <> ReferredUserId),
    CONSTRAINT CK_Referral_Status CHECK (Status IN ('Pending','Qualified','Rewarded','Rejected'))
    -- NOTE: there is intentionally NO ParentReferralId / UplineUserId / Level column.
    -- The model is flat by design (single-level) — see legal comment above.
);
GO
CREATE INDEX IX_Referral_Referrer ON dbo.Referrals (ReferrerUserId);
GO

/* ============================ 14. PLATFORM CREDIT (NON-CASHABLE) ==========
   LEGAL: "Credit" here is a NON-MONETARY platform loyalty point. It is:
     - NON-CASHABLE  : cannot be withdrawn, redeemed for, or converted to cash.
     - NON-TRANSFERABLE : cannot be sent/gifted/sold to another user.
     - SPENDABLE ONLY on platform services (e.g. featured-listing promotions).
   Therefore it is NOT electronic money / e-wallet and falls outside
   พ.ร.บ.ระบบการชำระเงิน 2560. There is deliberately NO withdraw/payout/cash-out
   table or column anywhere, and NO transfer-between-users path. Credit can EXPIRE
   (ExpiresAtUtc) which is incompatible with a stored-value money instrument.
   ========================================================================== */
CREATE TABLE dbo.CreditAccounts (
    UserId          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Balance         DECIMAL(12,2) NOT NULL CONSTRAINT DF_CreditAcc_Balance DEFAULT (0),  -- maintained from CreditTransactions ledger
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_CreditAcc_Created DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_CreditAcc_Updated DEFAULT (SYSUTCDATETIME()),
    RowVersion      ROWVERSION,
    CONSTRAINT FK_CreditAcc_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE,
    CONSTRAINT CK_CreditAcc_Balance CHECK (Balance >= 0)
);
GO

-- Append-only ledger. Balance changes are recorded here; never UPDATE/DELETE rows.
CREATE TABLE dbo.CreditTransactions (
    CreditTransactionId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    Amount          DECIMAL(12,2) NOT NULL,   -- positive = earn, negative = spend/expire
    Type            VARCHAR(20)   NOT NULL,
    RefId           NVARCHAR(64)  NULL,       -- loose ref to source entity (ReferralId / ListingPromotionId / etc.)
    -- B-02/G-2: idempotency guard against double-credit on retry/double-click/worker re-run.
    -- App MUST supply a stable key per logical credit operation (e.g. "Referral:{ReferralId}",
    -- "PromoSpend:{ListingPromotionId}"). Insert ledger row + balance update in one transaction.
    IdempotencyKey  NVARCHAR(100) NULL,
    BalanceAfter    DECIMAL(12,2) NOT NULL,
    ExpiresAtUtc    DATETIME2(3)  NULL,       -- set for earned credit that can expire; NULL for spend/adjust/expiry rows
    Note            NVARCHAR(400) NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_CreditTx_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_CreditTx_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    -- S-04: 'Revoke' = clawback of previously earned credit (e.g. referral abuse confirmed).
    --       Distinct from generic 'Adjustment' so FR-28/29 revoke is auditable & unambiguous.
    CONSTRAINT CK_CreditTx_Type CHECK (Type IN ('ReferralReward','PromoSpend','Adjustment','Expiry','Revoke')),
    CONSTRAINT CK_CreditTx_BalanceAfter CHECK (BalanceAfter >= 0)
    -- NOTE: no Withdraw/CashOut/Transfer type exists by design (non-cashable, non-transferable).
);
GO
CREATE INDEX IX_CreditTx_User ON dbo.CreditTransactions (UserId, CreatedAtUtc);
CREATE INDEX IX_CreditTx_Expiry ON dbo.CreditTransactions (ExpiresAtUtc) WHERE ExpiresAtUtc IS NOT NULL;
-- B-02/G-2: two-layer double-credit protection.
--   (1) explicit IdempotencyKey unique when supplied;
--   (2) (Type, RefId) unique when RefId supplied — one ledger row per source entity per type.
CREATE UNIQUE INDEX UX_CreditTx_Idempotency ON dbo.CreditTransactions (IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
CREATE UNIQUE INDEX UX_CreditTx_TypeRef ON dbo.CreditTransactions (Type, RefId) WHERE RefId IS NOT NULL;
GO

/* ============================ 15. FEATURED / PROMOTED LISTINGS (credit-paid) */
-- Lookup of promotion packages priced in platform credit.
CREATE TABLE dbo.PromotionPackages (
    PromotionPackageId TINYINT    NOT NULL PRIMARY KEY,
    Code            VARCHAR(20)   NOT NULL,
    DisplayName     NVARCHAR(60)  NOT NULL,
    PromotionType   VARCHAR(20)   NOT NULL,
    DurationDays    INT           NOT NULL,
    CreditCost      DECIMAL(12,2) NOT NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_PromoPkg_Active DEFAULT (1),
    CONSTRAINT UQ_PromoPkg_Code UNIQUE (Code),
    CONSTRAINT CK_PromoPkg_Type CHECK (PromotionType IN ('Featured','TopOfList','Highlight')),
    CONSTRAINT CK_PromoPkg_Duration CHECK (DurationDays > 0),
    CONSTRAINT CK_PromoPkg_Cost CHECK (CreditCost >= 0)
);
GO

CREATE TABLE dbo.ListingPromotions (
    ListingPromotionId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_ListPromo_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    UserId          UNIQUEIDENTIFIER NOT NULL,   -- buyer of the promotion (usually seller)
    PromotionPackageId TINYINT     NULL,
    PromotionType   VARCHAR(20)   NOT NULL,
    CreditCost      DECIMAL(12,2) NOT NULL,
    StartsAtUtc     DATETIME2(3)  NOT NULL CONSTRAINT DF_ListPromo_Start DEFAULT (SYSUTCDATETIME()),
    EndsAtUtc       DATETIME2(3)  NOT NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_ListPromo_Status DEFAULT ('Active'),
    -- Y-08: ledger row that debited the credit for this promotion (PromoSpend).
    -- NOT NULL + UNIQUE: a promotion is only created AFTER credit is successfully debited,
    -- and each ledger debit backs exactly one promotion (no missing/duplicated debit; pairs with B-02).
    CreditTransactionId BIGINT    NOT NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_ListPromo_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_ListPromo_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId),
    CONSTRAINT FK_ListPromo_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_ListPromo_Package FOREIGN KEY (PromotionPackageId) REFERENCES dbo.PromotionPackages (PromotionPackageId),
    CONSTRAINT FK_ListPromo_CreditTx FOREIGN KEY (CreditTransactionId) REFERENCES dbo.CreditTransactions (CreditTransactionId),
    CONSTRAINT UQ_ListPromo_CreditTx UNIQUE (CreditTransactionId),
    CONSTRAINT CK_ListPromo_Type CHECK (PromotionType IN ('Featured','TopOfList','Highlight')),
    CONSTRAINT CK_ListPromo_Status CHECK (Status IN ('Active','Expired','Cancelled')),
    CONSTRAINT CK_ListPromo_Time CHECK (EndsAtUtc > StartsAtUtc),
    CONSTRAINT CK_ListPromo_Cost CHECK (CreditCost >= 0)
);
GO
CREATE INDEX IX_ListPromo_Product ON dbo.ListingPromotions (ProductId);
-- active promotions sweep (BackgroundService expires them)
CREATE INDEX IX_ListPromo_Active_End ON dbo.ListingPromotions (EndsAtUtc) WHERE Status = 'Active';
GO

/* ============================ 16. PDPA : CONSENT & AUDIT ==================
   AUDIT NOTE: membership changes (signup/upgrade/renew/cancel), credit grants &
   spends (CreditTransactions), and referral rewards are all auditable business
   actions and MUST be written to dbo.AuditLogs (EntityType = 'Membership' /
   'CreditTransaction' / 'Referral' / 'ListingPromotion') by the app layer.
   ========================================================================== */
CREATE TABLE dbo.ConsentRecords (
    ConsentRecordId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    ConsentType     VARCHAR(40)   NOT NULL,
    DocumentVersion VARCHAR(20)   NOT NULL,
    IsGranted       BIT           NOT NULL,
    SourceIpHash    VARBINARY(32) NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Consent_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Consent_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId)
);
GO
CREATE INDEX IX_Consent_User_Type ON dbo.ConsentRecords (UserId, ConsentType, CreatedAtUtc);
GO

CREATE TABLE dbo.AuditLogs (
    AuditLogId      BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ActorUserId     UNIQUEIDENTIFIER NULL,
    Action          VARCHAR(60)   NOT NULL,
    EntityType      VARCHAR(60)   NOT NULL,
    EntityId        NVARCHAR(64)  NULL,
    BeforeJson      NVARCHAR(MAX) NULL,
    AfterJson       NVARCHAR(MAX) NULL,
    IpAddressHash   VARBINARY(32) NULL,
    -- S-06: per-transaction correlation id (NFR-A2) — ties together all audit rows produced
    -- within one logical business operation / HTTP request for AML & due-process tracing.
    CorrelationId   UNIQUEIDENTIFIER NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Audit_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Audit_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users (UserId)
);
GO
CREATE INDEX IX_Audit_Entity ON dbo.AuditLogs (EntityType, EntityId, CreatedAtUtc);
CREATE INDEX IX_Audit_Actor ON dbo.AuditLogs (ActorUserId, CreatedAtUtc);
CREATE INDEX IX_Audit_Correlation ON dbo.AuditLogs (CorrelationId) WHERE CorrelationId IS NOT NULL;
GO

/* ============================ 17. NOTIFICATIONS (B-01 / G-1) =============
   FR-27 mandates advance notice 14/3/1 days before trial/renewal expiry AND a LOG
   of every notice sent (auto-renew is only permitted AFTER advance notice was given).
   LEGAL #8 (consumer protection): we must be able to PROVE a notice was sent before
   any charge. This table is the audit trail for the notification worker and its
   idempotency guard (a milestone notice is sent at most once per membership).
   ========================================================================== */
CREATE TABLE dbo.Notifications (
    NotificationId  UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Notif_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER NOT NULL,
    Type            VARCHAR(30)   NOT NULL,
    Channel         VARCHAR(20)   NOT NULL,
    RelatedMembershipId UNIQUEIDENTIFIER NULL,
    -- Milestone = days-before-expiry bucket (14 / 3 / 1); NULL for non-lifecycle notices.
    Milestone       INT           NULL,
    ScheduledForUtc DATETIME2(3)  NOT NULL,
    SentAtUtc       DATETIME2(3)  NULL,
    Status          VARCHAR(20)   NOT NULL CONSTRAINT DF_Notif_Status DEFAULT ('Pending'),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Notif_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Notif_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Notif_Membership FOREIGN KEY (RelatedMembershipId) REFERENCES dbo.Memberships (MembershipId),
    CONSTRAINT CK_Notif_Type CHECK (Type IN ('TrialExpiring','RenewalDue','RenewalCharged','MembershipExpired','PromotionExpiring','PenaltyIssued','AppealUpdate','AuctionWon','AuctionClosed')),
    CONSTRAINT CK_Notif_Channel CHECK (Channel IN ('Email','InApp','Sms')),
    CONSTRAINT CK_Notif_Status CHECK (Status IN ('Pending','Sent','Failed','Skipped')),
    CONSTRAINT CK_Notif_Milestone CHECK (Milestone IS NULL OR Milestone IN (14,3,1))
);
GO
CREATE INDEX IX_Notif_User ON dbo.Notifications (UserId, CreatedAtUtc);
-- worker picks due, not-yet-sent notices
CREATE INDEX IX_Notif_Due ON dbo.Notifications (ScheduledForUtc) WHERE Status = 'Pending';
-- B-01/G-1: dedupe — at most one notice per (user, type, membership, milestone) so the
-- worker can re-run safely without double-notifying. Filtered to lifecycle notices.
CREATE UNIQUE INDEX UX_Notif_NoDup
    ON dbo.Notifications (UserId, Type, RelatedMembershipId, Milestone)
    WHERE RelatedMembershipId IS NOT NULL AND Milestone IS NOT NULL;
GO

/* ============================ 18. APPRAISAL OPINIONS (B-03 / G-3) =========
   FR-07 / IS-8: independent appraiser opinions on a product. LEGAL #2: the platform
   must NOT warrant authenticity; every opinion is stored with a DisclaimerVersion and
   is an OPINION only (UI must avoid "รับประกัน/ของแท้ 100%"). Storing the disclaimer
   version per opinion lets us prove which disclaimer text was shown at the time.
   ========================================================================== */
CREATE TABLE dbo.AppraisalOpinions (
    AppraisalOpinionId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Appraisal_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    -- Appraiser may be a platform user (FK) and/or an external named expert.
    AppraiserUserId UNIQUEIDENTIFIER NULL,
    AppraiserName   NVARCHAR(120) NOT NULL,
    OpinionText     NVARCHAR(MAX) NOT NULL,
    DisclaimerVersion VARCHAR(20) NOT NULL,
    IsPublished     BIT           NOT NULL CONSTRAINT DF_Appraisal_Pub DEFAULT (0),
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_Appraisal_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Appraisal_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products (ProductId),
    CONSTRAINT FK_Appraisal_User FOREIGN KEY (AppraiserUserId) REFERENCES dbo.Users (UserId)
);
GO
CREATE INDEX IX_Appraisal_Product ON dbo.AppraisalOpinions (ProductId) WHERE IsPublished = 1;
GO

/* ============================ 19. CONFIG VERSIONING (B-04 / G-4) ==========
   FR-31: admin-editable config (prices, credit costs, KYC value threshold, packages)
   MUST keep a versioned history with an effective date and MUST NOT apply retroactively
   to already-paid cycles. MembershipTiers/PromotionPackages stay as the "current" lookup
   for convenience, but every change is appended here as an immutable, dated version.
   The actual charged price is also snapshotted on Memberships.PaidAmountTHB / FeeInvoices
   (Y-06) so a price change can never alter what a user already paid.
   ========================================================================== */
CREATE TABLE dbo.ConfigVersions (
    ConfigVersionId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ConfigKey       VARCHAR(80)   NOT NULL,   -- e.g. 'MembershipTier.Premium.AnnualPriceTHB', 'Kyc.ValueThresholdTHB'
    Value           NVARCHAR(400) NOT NULL,   -- string-encoded value (number/json) interpreted by app
    EffectiveFromUtc DATETIME2(3) NOT NULL,
    CreatedByUserId UNIQUEIDENTIFIER NULL,    -- admin who made the change (null = system/seed)
    Note            NVARCHAR(400) NULL,
    CreatedAtUtc    DATETIME2(3)  NOT NULL CONSTRAINT DF_ConfigVer_Created DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_ConfigVer_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users (UserId),
    -- one config version per key per effective instant (no ambiguous overlapping versions)
    CONSTRAINT UQ_ConfigVer_KeyEffective UNIQUE (ConfigKey, EffectiveFromUtc)
);
GO
-- resolve "value of key K at time T" = latest EffectiveFromUtc <= T
CREATE INDEX IX_ConfigVer_Key_Effective ON dbo.ConfigVersions (ConfigKey, EffectiveFromUtc DESC);
GO

/* ============================ 21. NOTIFICATION DELIVERY LOG (M1) ==========
   APPEND-ONLY evidence of every real outbound Email/SMS send attempt through a concrete
   provider. LEGAL #8 / FR-27 / FR-32: prove a notice was handed to a provider, to whom,
   when, and with what outcome — independent of the lifecycle dbo.Notifications row.
   PDPA: RecipientMasked stores a masked address only (never raw email/phone); the row is
   purged after RetentionExpiresAtUtc by a retention worker.
   ========================================================================== */
CREATE TABLE dbo.NotificationDeliveryLog (
    NotificationDeliveryLogId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    NotificationId      UNIQUEIDENTIFIER NULL,
    UserId              UNIQUEIDENTIFIER NULL,
    Provider            VARCHAR(40)   NOT NULL,   -- 'SendGrid','SES','TwilioSms','SmtpDev','SmsMock'
    ProviderMessageId   VARCHAR(200)  NULL,
    Channel             VARCHAR(20)   NOT NULL,
    RecipientMasked     VARCHAR(120)  NOT NULL,   -- masked; never raw PII
    TemplateKey         VARCHAR(80)   NOT NULL,
    TemplateVersion     VARCHAR(20)   NOT NULL,
    Status              VARCHAR(20)   NOT NULL CONSTRAINT DF_NotifDelivery_Status DEFAULT ('Queued'),
    ErrorDetail         NVARCHAR(1000) NULL,
    AttemptCount        INT           NOT NULL CONSTRAINT DF_NotifDelivery_Attempt DEFAULT (0),
    PayloadSnapshotJson NVARCHAR(MAX) NULL,       -- rendered template vars; no secrets / no raw PII
    CorrelationId       UNIQUEIDENTIFIER NULL,
    SentAtUtc           DATETIME2(3)  NULL,
    CreatedAtUtc        DATETIME2(3)  NOT NULL CONSTRAINT DF_NotifDelivery_Created DEFAULT (SYSUTCDATETIME()),
    RetentionExpiresAtUtc DATETIME2(3) NOT NULL,
    CONSTRAINT FK_NotifDelivery_Notification FOREIGN KEY (NotificationId) REFERENCES dbo.Notifications (NotificationId),
    CONSTRAINT FK_NotifDelivery_User FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT CK_NotifDelivery_Channel CHECK (Channel IN ('Email','Sms')),
    CONSTRAINT CK_NotifDelivery_Status CHECK (Status IN ('Queued','Sent','Failed','Retrying'))
);
GO
CREATE INDEX IX_NotifDelivery_User ON dbo.NotificationDeliveryLog (UserId, CreatedAtUtc);
CREATE INDEX IX_NotifDelivery_Correlation ON dbo.NotificationDeliveryLog (CorrelationId) WHERE CorrelationId IS NOT NULL;
CREATE INDEX IX_NotifDelivery_Retention ON dbo.NotificationDeliveryLog (RetentionExpiresAtUtc);
GO

/* ============================ 22. PDPA DSAR (M3) ==========================
   Data-subject access/erasure requests (FR-01/04). Tracks Export/Erasure/Access/Rectify/
   WithdrawConsent with a statutory DueByUtc SLA. Stores only the export artifact PATH (never
   the data inline); erasure reuses the soft-delete/anonymize on dbo.Users. Status transitions
   are mirrored to dbo.AuditLogs by the app layer.
   ========================================================================== */
CREATE TABLE dbo.DataSubjectRequests (
    DataSubjectRequestId UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT DF_Dsar_Id DEFAULT (NEWSEQUENTIALID()) PRIMARY KEY,
    RequestType         VARCHAR(20)   NOT NULL,
    Status              VARCHAR(20)   NOT NULL CONSTRAINT DF_Dsar_Status DEFAULT ('Pending'),
    RequestedByUserId   UNIQUEIDENTIFIER NOT NULL,
    VerifiedAtUtc       DATETIME2(3)  NULL,
    HandledByUserId     UNIQUEIDENTIFIER NULL,
    ResultArtifactPath  NVARCHAR(400) NULL,
    DueByUtc            DATETIME2(3)  NOT NULL,
    Note                NVARCHAR(2000) NULL,
    CreatedAtUtc        DATETIME2(3)  NOT NULL CONSTRAINT DF_Dsar_Created DEFAULT (SYSUTCDATETIME()),
    CompletedAtUtc      DATETIME2(3)  NULL,
    CONSTRAINT FK_Dsar_Requester FOREIGN KEY (RequestedByUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_Dsar_HandledBy FOREIGN KEY (HandledByUserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT CK_Dsar_Type CHECK (RequestType IN ('Export','Erasure','Access','Rectify','WithdrawConsent')),
    CONSTRAINT CK_Dsar_Status CHECK (Status IN ('Pending','InProgress','Completed','Rejected'))
);
GO
CREATE INDEX IX_Dsar_Requester ON dbo.DataSubjectRequests (RequestedByUserId, CreatedAtUtc);
CREATE INDEX IX_Dsar_Status_Due ON dbo.DataSubjectRequests (Status, DueByUtc);
GO

/* ============================ 14. SEED LOOKUP DATA ======================== */
INSERT INTO dbo.KycStatuses (KycStatusId, Code, DisplayName) VALUES
    (0,'NONE',     N'Not verified'),
    (1,'PENDING',  N'Pending review'),
    (2,'VERIFIED', N'Verified'),
    (3,'REJECTED', N'Rejected'),
    (4,'EXPIRED',  N'Expired'),
    -- M2 (NDID e-KYC): provider redirect started, awaiting async callback/IAL result.
    (5,'INITIATED',N'Initiated (awaiting provider)');
GO

-- Annual membership tiers: Normal=1000, Verified=1500, Premium=2000 (THB/year).
-- Verified & Premium require completed KYC.
INSERT INTO dbo.MembershipTiers (MembershipTierId, Code, DisplayName, RequiresKyc, MonthlyFee, AnnualPriceTHB) VALUES
    (1,'Normal',   N'Normal',   0, 0.00, 1000.00),
    (2,'Verified', N'Verified', 1, 0.00, 1500.00),
    (3,'Premium',  N'Premium',  1, 0.00, 2000.00);
GO

INSERT INTO dbo.PromotionPackages (PromotionPackageId, Code, DisplayName, PromotionType, DurationDays, CreditCost) VALUES
    (1,'FEAT_7',   N'Featured 7 days',     'Featured',   7,  100.00),
    (2,'TOP_3',    N'Top of list 3 days',  'TopOfList',  3,   80.00),
    (3,'HL_7',     N'Highlight 7 days',    'Highlight',  7,   50.00);
GO

-- Codes 1-6 are penalties (negative). Code 7 COMPLETED_DEAL is a positive trust reward applied by
-- TrustScoreService when a buyer confirms receipt (FR-19 -> FR-14); kept here so the reward is not fail-soft.
INSERT INTO dbo.BlacklistReasonCodes (ReasonCodeId, Code, DisplayName, Severity, DefaultScorePenalty) VALUES
    (1,'LATE_SHIPMENT',    N'Late shipment',          1, -20),
    (2,'NO_RESPONSE',      N'Unresponsive',           1, -20),
    (3,'ITEM_NOT_AS_DESC', N'Item not as described',  2, -20),
    (4,'SHILL_BIDDING',    N'Shill bidding',          3, -40),
    (5,'FAKE_ITEM',        N'Counterfeit item',       4, -60),
    (6,'FRAUD_PAYMENT',    N'Payment fraud',          5, -100),
    (7,'COMPLETED_DEAL',   N'Completed deal',         1,   5);
GO

-- ConfigVersions baseline (B-04/G-4, FR-27/FR-28/FR-31). Read via ConfigVersionResolver: "value of key K
-- at time T = latest EffectiveFromUtc <= T". Baseline is effective from the platform epoch (2020-01-01) so
-- any later admin change (more recent EffectiveFromUtc) always overrides it. Mirrors SeedData.cs HasData().
-- NOTE(business): Referral.* amounts/expiry are PLACEHOLDERS pending sign-off — add a new row (newer
-- EffectiveFromUtc) to change them; never UPDATE an existing row (immutable config history).
INSERT INTO dbo.ConfigVersions (ConfigKey, Value, EffectiveFromUtc, CreatedByUserId, Note) VALUES
    ('MembershipTier.Normal.AnnualPriceTHB',   N'1000', '2020-01-01T00:00:00', NULL, N'seed: baseline annual price (FR-27)'),
    ('MembershipTier.Verified.AnnualPriceTHB', N'1500', '2020-01-01T00:00:00', NULL, N'seed: baseline annual price (FR-27)'),
    ('MembershipTier.Premium.AnnualPriceTHB',  N'2000', '2020-01-01T00:00:00', NULL, N'seed: baseline annual price (FR-27)'),
    ('Membership.TrialMonths',                 N'3',    '2020-01-01T00:00:00', NULL, N'seed: free-trial length (FR-27)'),
    ('Referral.RewardCreditToReferrer',        N'100',  '2020-01-01T00:00:00', NULL, N'seed: PLACEHOLDER pending business sign-off (FR-28)'),
    ('Referral.RewardCreditToReferred',        N'50',   '2020-01-01T00:00:00', NULL, N'seed: PLACEHOLDER pending business sign-off (FR-28)'),
    ('Referral.CreditExpiryDays',              N'365',  '2020-01-01T00:00:00', NULL, N'seed: PLACEHOLDER pending business sign-off (FR-28)');
GO

/* ============================ 20. APPEND-ONLY ENFORCEMENT (B-06 / G-6) ====
   FR-24 / FR-29 / NFR-A1: AuditLogs, ConsentRecords and CreditTransactions are
   IMMUTABLE / append-only. Any attempt to UPDATE or DELETE them MUST be rejected by
   the database itself (not merely by app convention), so that even direct DB access
   cannot tamper with audit / consent / credit-ledger history (AML & due-process).

   Enforced here with INSTEAD OF UPDATE, DELETE triggers that THROW. Triggers are created
   AFTER all tables exist. INSERT is intentionally NOT intercepted (append is allowed).

   ALTERNATIVE HARDENING (apply at real deploy, in addition to or instead of triggers):
     - DENY UPDATE, DELETE ON dbo.AuditLogs/ConsentRecords/CreditTransactions TO [app_role];
       (defence-in-depth at the permission layer; triggers still guard sysadmin/dbo access)
     - SQL Server 2022 ledger tables (CREATE TABLE ... WITH (LEDGER = ON)) or system-
       versioned temporal tables for cryptographically verifiable immutability.
   ========================================================================== */
GO
CREATE TRIGGER TR_AuditLogs_NoModify ON dbo.AuditLogs
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51001, 'AuditLogs is append-only (FR-24). UPDATE/DELETE is forbidden.', 1;
END;
GO
CREATE TRIGGER TR_ConsentRecords_NoModify ON dbo.ConsentRecords
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51002, 'ConsentRecords is append-only (PDPA). UPDATE/DELETE is forbidden; record a new consent event instead.', 1;
END;
GO
CREATE TRIGGER TR_CreditTransactions_NoModify ON dbo.CreditTransactions
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51003, 'CreditTransactions ledger is append-only (FR-29). UPDATE/DELETE is forbidden; post a correcting Adjustment/Revoke row instead.', 1;
END;
GO
-- M1: provider-send evidence is append-only (LEGAL #8). To record a status change, append a NEW row
-- with the same CorrelationId — never UPDATE. NOTE: the PDPA retention-purge worker must run under a
-- principal/role that bypasses this trigger (or temporarily DISABLE it) to physically delete expired rows.
CREATE TRIGGER TR_NotifDelivery_NoModify ON dbo.NotificationDeliveryLog
INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51004, 'NotificationDeliveryLog is append-only (LEGAL #8). UPDATE/DELETE is forbidden; append a new attempt row instead.', 1;
END;
GO

PRINT 'Marketplace MVP schema created successfully.';
GO
