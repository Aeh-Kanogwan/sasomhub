using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Marketplace.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "BlacklistReasonCodes",
                schema: "dbo",
                columns: table => new
                {
                    ReasonCodeId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(40)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    DefaultScorePenalty = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistReasonCodes", x => x.ReasonCodeId);
                    table.CheckConstraint("CK_ReasonCodes_Severity", "[Severity] BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                schema: "dbo",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentCategoryId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "varchar(140)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                    table.ForeignKey(
                        name: "FK_Categories_Parent",
                        column: x => x.ParentCategoryId,
                        principalSchema: "dbo",
                        principalTable: "Categories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KycStatuses",
                schema: "dbo",
                columns: table => new
                {
                    KycStatusId = table.Column<byte>(type: "tinyint", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycStatuses", x => x.KycStatusId);
                });

            migrationBuilder.CreateTable(
                name: "MembershipTiers",
                schema: "dbo",
                columns: table => new
                {
                    MembershipTierId = table.Column<byte>(type: "tinyint", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequiresKyc = table.Column<bool>(type: "bit", nullable: false),
                    MonthlyFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    AnnualPriceTHB = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipTiers", x => x.MembershipTierId);
                    table.CheckConstraint("CK_Tier_AnnualPrice", "[AnnualPriceTHB] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PromotionPackages",
                schema: "dbo",
                columns: table => new
                {
                    PromotionPackageId = table.Column<byte>(type: "tinyint", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PromotionType = table.Column<string>(type: "varchar(20)", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    CreditCost = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionPackages", x => x.PromotionPackageId);
                    table.CheckConstraint("CK_PromoPkg_Cost", "[CreditCost] >= 0");
                    table.CheckConstraint("CK_PromoPkg_Duration", "[DurationDays] > 0");
                    table.CheckConstraint("CK_PromoPkg_Type", "[PromotionType] IN ('Featured','TopOfList','Highlight')");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Role = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Member"),
                    AccountStatus = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Active"),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    IsAnonymized = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.CheckConstraint("CK_Users_Role", "[Role] IN ('Member','Admin','Support')");
                    table.CheckConstraint("CK_Users_Status", "[AccountStatus] IN ('Active','Suspended','PendingBanReview','Banned')");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "dbo",
                columns: table => new
                {
                    AuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "varchar(60)", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(60)", nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddressHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.AuditLogId);
                    table.ForeignKey(
                        name: "FK_Audit_Actor",
                        column: x => x.ActorUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "BlacklistEntries",
                schema: "dbo",
                columns: table => new
                {
                    BlacklistEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonCodeId = table.Column<int>(type: "int", nullable: false),
                    InternalNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewStatus = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "PendingReview"),
                    AppealStatus = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "None"),
                    AppealNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistEntries", x => x.BlacklistEntryId);
                    table.CheckConstraint("CK_Blacklist_Appeal", "[AppealStatus] IN ('None','Requested','UnderReview','Accepted','Denied')");
                    table.CheckConstraint("CK_Blacklist_Review", "[ReviewStatus] IN ('PendingReview','Confirmed','Rejected','Appealed','Overturned')");
                    table.ForeignKey(
                        name: "FK_Blacklist_CreatedBy",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Blacklist_Reason",
                        column: x => x.ReasonCodeId,
                        principalSchema: "dbo",
                        principalTable: "BlacklistReasonCodes",
                        principalColumn: "ReasonCodeId");
                    table.ForeignKey(
                        name: "FK_Blacklist_ReviewedBy",
                        column: x => x.ReviewedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Blacklist_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ConfigVersions",
                schema: "dbo",
                columns: table => new
                {
                    ConfigVersionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigKey = table.Column<string>(type: "varchar(80)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigVersions", x => x.ConfigVersionId);
                    table.ForeignKey(
                        name: "FK_ConfigVer_CreatedBy",
                        column: x => x.CreatedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                schema: "dbo",
                columns: table => new
                {
                    ConsentRecordId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsentType = table.Column<string>(type: "varchar(40)", nullable: false),
                    DocumentVersion = table.Column<string>(type: "varchar(20)", nullable: false),
                    IsGranted = table.Column<bool>(type: "bit", nullable: false),
                    SourceIpHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.ConsentRecordId);
                    table.ForeignKey(
                        name: "FK_Consent_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "CreditAccounts",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", nullable: false, defaultValue: 0m),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditAccounts", x => x.UserId);
                    table.CheckConstraint("CK_CreditAcc_Balance", "[Balance] >= 0");
                    table.ForeignKey(
                        name: "FK_CreditAcc_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditTransactions",
                schema: "dbo",
                columns: table => new
                {
                    CreditTransactionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Type = table.Column<string>(type: "varchar(20)", nullable: false),
                    RefId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BalanceAfter = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditTransactions", x => x.CreditTransactionId);
                    table.CheckConstraint("CK_CreditTx_BalanceAfter", "[BalanceAfter] >= 0");
                    table.CheckConstraint("CK_CreditTx_Type", "[Type] IN ('ReferralReward','PromoSpend','Adjustment','Expiry','Revoke')");
                    table.ForeignKey(
                        name: "FK_CreditTx_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "KycVerifications",
                schema: "dbo",
                columns: table => new
                {
                    KycVerificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KycStatusId = table.Column<byte>(type: "tinyint", nullable: false),
                    Provider = table.Column<string>(type: "varchar(40)", nullable: false),
                    ProviderReference = table.Column<string>(type: "varchar(128)", nullable: true),
                    VerificationLevel = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycVerifications", x => x.KycVerificationId);
                    table.ForeignKey(
                        name: "FK_Kyc_Status",
                        column: x => x.KycStatusId,
                        principalSchema: "dbo",
                        principalTable: "KycStatuses",
                        principalColumn: "KycStatusId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Kyc_Users",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                schema: "dbo",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipTierId = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Trial"),
                    StartAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    EndAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    TrialStartsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    TrialEndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PaidThroughUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false),
                    PaidAmountTHB = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.MembershipId);
                    table.CheckConstraint("CK_Membership_PaidAmount", "[PaidAmountTHB] IS NULL OR [PaidAmountTHB] >= 0");
                    table.CheckConstraint("CK_Membership_Status", "[Status] IN ('Trial','Active','Expired','Cancelled')");
                    table.CheckConstraint("CK_Membership_Trial", "[TrialEndsAtUtc] IS NULL OR [TrialStartsAtUtc] IS NULL OR [TrialEndsAtUtc] > [TrialStartsAtUtc]");
                    table.CheckConstraint("CK_Membership_TrialHasEnd", "[Status] <> 'Trial' OR [TrialEndsAtUtc] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Membership_Tier",
                        column: x => x.MembershipTierId,
                        principalSchema: "dbo",
                        principalTable: "MembershipTiers",
                        principalColumn: "MembershipTierId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Membership_Users",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                schema: "dbo",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConditionGrade = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Used"),
                    Rarity = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "common"),
                    SerialLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ListingType = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "FixedPrice"),
                    FixedPrice = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    Currency = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "THB"),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Draft"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                    table.CheckConstraint("CK_Products_FixedPrice", "[FixedPrice] IS NULL OR [FixedPrice] >= 0");
                    table.CheckConstraint("CK_Products_Rarity", "[Rarity] IN ('common','rare','epic','legendary')");
                    table.CheckConstraint("CK_Products_Status", "[Status] IN ('Draft','Active','Sold','Closed','Removed')");
                    table.CheckConstraint("CK_Products_Type", "[ListingType] IN ('FixedPrice','Auction')");
                    table.ForeignKey(
                        name: "FK_Products_Category",
                        column: x => x.CategoryId,
                        principalSchema: "dbo",
                        principalTable: "Categories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Seller",
                        column: x => x.SellerId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralCodes",
                schema: "dbo",
                columns: table => new
                {
                    ReferralCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralCodes", x => x.ReferralCodeId);
                    table.ForeignKey(
                        name: "FK_RefCode_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                schema: "dbo",
                columns: table => new
                {
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ReferrerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferredUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralCode = table.Column<string>(type: "varchar(20)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Pending"),
                    RewardCreditToReferrer = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    RewardCreditToReferred = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 0m),
                    RewardedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.ReferralId);
                    table.CheckConstraint("CK_Referral_NotSelf", "[ReferrerUserId] <> [ReferredUserId]");
                    table.CheckConstraint("CK_Referral_Status", "[Status] IN ('Pending','Qualified','Rewarded','Rejected')");
                    table.ForeignKey(
                        name: "FK_Referral_Referred",
                        column: x => x.ReferredUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Referral_Referrer",
                        column: x => x.ReferrerUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "TrustScores",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    LastCalculatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustScores", x => x.UserId);
                    table.CheckConstraint("CK_TrustScore_Range", "[Score] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_TrustScores_Users",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                schema: "dbo",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Bio = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProvinceCode = table.Column<string>(type: "varchar(10)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_Users",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KycSensitiveData",
                schema: "dbo",
                columns: table => new
                {
                    KycVerificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullNameMasked = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NationalIdHash = table.Column<byte[]>(type: "varbinary(64)", nullable: true),
                    RetentionExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycSensitiveData", x => x.KycVerificationId);
                    table.ForeignKey(
                        name: "FK_KycSens_Kyc",
                        column: x => x.KycVerificationId,
                        principalSchema: "dbo",
                        principalTable: "KycVerifications",
                        principalColumn: "KycVerificationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "dbo",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "varchar(30)", nullable: false),
                    Channel = table.Column<string>(type: "varchar(20)", nullable: false),
                    RelatedMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Milestone = table.Column<int>(type: "int", nullable: true),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Pending"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.CheckConstraint("CK_Notif_Channel", "[Channel] IN ('Email','InApp','Sms')");
                    table.CheckConstraint("CK_Notif_Milestone", "[Milestone] IS NULL OR [Milestone] IN (14,3,1)");
                    table.CheckConstraint("CK_Notif_Status", "[Status] IN ('Pending','Sent','Failed','Skipped')");
                    table.CheckConstraint("CK_Notif_Type", "[Type] IN ('TrialExpiring','RenewalDue','RenewalCharged','MembershipExpired','PromotionExpiring','PenaltyIssued','AppealUpdate')");
                    table.ForeignKey(
                        name: "FK_Notif_Membership",
                        column: x => x.RelatedMembershipId,
                        principalSchema: "dbo",
                        principalTable: "Memberships",
                        principalColumn: "MembershipId");
                    table.ForeignKey(
                        name: "FK_Notif_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "AppraisalOpinions",
                schema: "dbo",
                columns: table => new
                {
                    AppraisalOpinionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppraiserUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppraiserName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OpinionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisclaimerVersion = table.Column<string>(type: "varchar(20)", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppraisalOpinions", x => x.AppraisalOpinionId);
                    table.ForeignKey(
                        name: "FK_Appraisal_Product",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Products",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_Appraisal_User",
                        column: x => x.AppraiserUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "FeeInvoices",
                schema: "dbo",
                columns: table => new
                {
                    FeeInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeeType = table.Column<string>(type: "varchar(20)", nullable: false),
                    RelatedMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "THB"),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Issued"),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ExternalPaymentRef = table.Column<string>(type: "varchar(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeInvoices", x => x.FeeInvoiceId);
                    table.CheckConstraint("CK_Fee_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_Fee_Status", "[Status] IN ('Issued','Paid','Void','Overdue')");
                    table.CheckConstraint("CK_Fee_Type", "[FeeType] IN ('Membership','MembershipRenewal','MembershipUpgrade','Listing','Premium','Featured')");
                    table.ForeignKey(
                        name: "FK_Fee_Membership",
                        column: x => x.RelatedMembershipId,
                        principalSchema: "dbo",
                        principalTable: "Memberships",
                        principalColumn: "MembershipId");
                    table.ForeignKey(
                        name: "FK_Fee_Product",
                        column: x => x.RelatedProductId,
                        principalSchema: "dbo",
                        principalTable: "Products",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_Fee_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ListingPromotions",
                schema: "dbo",
                columns: table => new
                {
                    ListingPromotionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PromotionPackageId = table.Column<byte>(type: "tinyint", nullable: true),
                    PromotionType = table.Column<string>(type: "varchar(20)", nullable: false),
                    CreditCost = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Active"),
                    CreditTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingPromotions", x => x.ListingPromotionId);
                    table.CheckConstraint("CK_ListPromo_Cost", "[CreditCost] >= 0");
                    table.CheckConstraint("CK_ListPromo_Status", "[Status] IN ('Active','Expired','Cancelled')");
                    table.CheckConstraint("CK_ListPromo_Time", "[EndsAtUtc] > [StartsAtUtc]");
                    table.CheckConstraint("CK_ListPromo_Type", "[PromotionType] IN ('Featured','TopOfList','Highlight')");
                    table.ForeignKey(
                        name: "FK_ListPromo_CreditTx",
                        column: x => x.CreditTransactionId,
                        principalSchema: "dbo",
                        principalTable: "CreditTransactions",
                        principalColumn: "CreditTransactionId");
                    table.ForeignKey(
                        name: "FK_ListPromo_Package",
                        column: x => x.PromotionPackageId,
                        principalSchema: "dbo",
                        principalTable: "PromotionPackages",
                        principalColumn: "PromotionPackageId");
                    table.ForeignKey(
                        name: "FK_ListPromo_Product",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Products",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ListPromo_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                schema: "dbo",
                columns: table => new
                {
                    ProductImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.ProductImageId);
                    table.ForeignKey(
                        name: "FK_ProductImages_Product",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Auctions",
                schema: "dbo",
                columns: table => new
                {
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartingPrice = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    ReservePrice = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    BidIncrement = table.Column<decimal>(type: "decimal(12,2)", nullable: false, defaultValue: 1m),
                    CurrentHighBid = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    WinningBidId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Scheduled"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auctions", x => x.AuctionId);
                    table.CheckConstraint("CK_Auctions_Prices", "[StartingPrice] >= 0 AND ([ReservePrice] IS NULL OR [ReservePrice] >= [StartingPrice])");
                    table.CheckConstraint("CK_Auctions_Status", "[Status] IN ('Scheduled','Open','Closed','Cancelled')");
                    table.CheckConstraint("CK_Auctions_Time", "[EndAtUtc] > [StartAtUtc]");
                    table.ForeignKey(
                        name: "FK_Auctions_Product",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Bids",
                schema: "dbo",
                columns: table => new
                {
                    BidId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    AuctionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BidderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Active"),
                    IpAddressHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    DeviceFingerprintHash = table.Column<byte[]>(type: "varbinary(32)", nullable: true),
                    IsFlaggedShill = table.Column<bool>(type: "bit", nullable: false),
                    RelationshipFlag = table.Column<string>(type: "varchar(30)", nullable: true),
                    PlacedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bids", x => x.BidId);
                    table.CheckConstraint("CK_Bids_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Bids_Status", "[Status] IN ('Active','Outbid','Won','Retracted','Voided')");
                    table.ForeignKey(
                        name: "FK_Bids_Auction",
                        column: x => x.AuctionId,
                        principalSchema: "dbo",
                        principalTable: "Auctions",
                        principalColumn: "AuctionId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bids_Bidder",
                        column: x => x.BidderId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                schema: "dbo",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WinningBidId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgreedAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "THB"),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Pending"),
                    ExternalPaymentNote = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    TransferredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.TransactionId);
                    table.CheckConstraint("CK_Tx_Amount", "[AgreedAmount] >= 0");
                    table.CheckConstraint("CK_Tx_Parties", "[BuyerId] <> [SellerId]");
                    table.CheckConstraint("CK_Tx_Status", "[Status] IN ('Pending','Transferred','Confirmed','Disputed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_Tx_Buyer",
                        column: x => x.BuyerId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tx_Product",
                        column: x => x.ProductId,
                        principalSchema: "dbo",
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tx_Seller",
                        column: x => x.SellerId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tx_WinningBid",
                        column: x => x.WinningBidId,
                        principalSchema: "dbo",
                        principalTable: "Bids",
                        principalColumn: "BidId");
                });

            migrationBuilder.CreateTable(
                name: "Disputes",
                schema: "dbo",
                columns: table => new
                {
                    DisputeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaisedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonCodeId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", nullable: false, defaultValue: "Open"),
                    Resolution = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HandledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disputes", x => x.DisputeId);
                    table.CheckConstraint("CK_Dispute_Status", "[Status] IN ('Open','UnderReview','Resolved','Rejected','Escalated')");
                    table.ForeignKey(
                        name: "FK_Dispute_HandledBy",
                        column: x => x.HandledByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Dispute_RaisedBy",
                        column: x => x.RaisedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Dispute_Reason",
                        column: x => x.ReasonCodeId,
                        principalSchema: "dbo",
                        principalTable: "BlacklistReasonCodes",
                        principalColumn: "ReasonCodeId");
                    table.ForeignKey(
                        name: "FK_Dispute_Tx",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "Transactions",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PenaltyActions",
                schema: "dbo",
                columns: table => new
                {
                    PenaltyActionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<string>(type: "varchar(20)", nullable: false),
                    ReasonCodeId = table.Column<int>(type: "int", nullable: false),
                    RelatedTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyActions", x => x.PenaltyActionId);
                    table.CheckConstraint("CK_Penalty_Type", "[ActionType] IN ('Warning','ScoreDeduct','Suspend','Ban')");
                    table.ForeignKey(
                        name: "FK_Penalty_IssuedBy",
                        column: x => x.IssuedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Penalty_Reason",
                        column: x => x.ReasonCodeId,
                        principalSchema: "dbo",
                        principalTable: "BlacklistReasonCodes",
                        principalColumn: "ReasonCodeId");
                    table.ForeignKey(
                        name: "FK_Penalty_Tx",
                        column: x => x.RelatedTransactionId,
                        principalSchema: "dbo",
                        principalTable: "Transactions",
                        principalColumn: "TransactionId");
                    table.ForeignKey(
                        name: "FK_Penalty_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                schema: "dbo",
                columns: table => new
                {
                    ReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevieweeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.ReviewId);
                    table.CheckConstraint("CK_Reviews_Parties", "[ReviewerId] <> [RevieweeId]");
                    table.CheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_Reviews_Reviewee",
                        column: x => x.RevieweeId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Reviews_Reviewer",
                        column: x => x.ReviewerId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                    table.ForeignKey(
                        name: "FK_Reviews_Tx",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "Transactions",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionStatusHistory",
                schema: "dbo",
                columns: table => new
                {
                    TransactionStatusHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "varchar(20)", nullable: true),
                    ToStatus = table.Column<string>(type: "varchar(20)", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionStatusHistory", x => x.TransactionStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_TxHist_Tx",
                        column: x => x.TransactionId,
                        principalSchema: "dbo",
                        principalTable: "Transactions",
                        principalColumn: "TransactionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TxHist_User",
                        column: x => x.ChangedByUserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "TrustScoreHistory",
                schema: "dbo",
                columns: table => new
                {
                    TrustScoreHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Delta = table.Column<int>(type: "int", nullable: false),
                    ScoreAfter = table.Column<int>(type: "int", nullable: false),
                    ReasonCodeId = table.Column<int>(type: "int", nullable: true),
                    RelatedTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustScoreHistory", x => x.TrustScoreHistoryId);
                    table.ForeignKey(
                        name: "FK_TSHist_Reason",
                        column: x => x.ReasonCodeId,
                        principalSchema: "dbo",
                        principalTable: "BlacklistReasonCodes",
                        principalColumn: "ReasonCodeId");
                    table.ForeignKey(
                        name: "FK_TSHist_Tx",
                        column: x => x.RelatedTransactionId,
                        principalSchema: "dbo",
                        principalTable: "Transactions",
                        principalColumn: "TransactionId");
                    table.ForeignKey(
                        name: "FK_TSHist_User",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "BlacklistReasonCodes",
                columns: new[] { "ReasonCodeId", "Code", "DefaultScorePenalty", "DisplayName", "IsActive", "Severity" },
                values: new object[,]
                {
                    { 1, "LATE_SHIPMENT", -20, "Late shipment", true, (byte)1 },
                    { 2, "NO_RESPONSE", -20, "Unresponsive", true, (byte)1 },
                    { 3, "ITEM_NOT_AS_DESC", -20, "Item not as described", true, (byte)2 },
                    { 4, "SHILL_BIDDING", -40, "Shill bidding", true, (byte)3 },
                    { 5, "FAKE_ITEM", -60, "Counterfeit item", true, (byte)4 },
                    { 6, "FRAUD_PAYMENT", -100, "Payment fraud", true, (byte)5 },
                    { 7, "COMPLETED_DEAL", 5, "Completed deal", true, (byte)1 }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Categories",
                columns: new[] { "CategoryId", "CreatedAtUtc", "IsActive", "Name", "ParentCategoryId", "Slug" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Coins & Banknotes", null, "coins-banknotes" },
                    { 2, new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Stamps", null, "stamps" },
                    { 3, new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Trading Cards", null, "trading-cards" },
                    { 4, new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Amulets", null, "amulets" },
                    { 5, new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), true, "Figures & Toys", null, "figures-toys" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "ConfigVersions",
                columns: new[] { "ConfigVersionId", "ConfigKey", "CreatedAtUtc", "CreatedByUserId", "EffectiveFromUtc", "Note", "Value" },
                values: new object[,]
                {
                    { 1L, "MembershipTier.Normal.AnnualPriceTHB", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: baseline annual price (FR-27)", "1000" },
                    { 2L, "MembershipTier.Verified.AnnualPriceTHB", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: baseline annual price (FR-27)", "1500" },
                    { 3L, "MembershipTier.Premium.AnnualPriceTHB", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: baseline annual price (FR-27)", "2000" },
                    { 4L, "Membership.TrialMonths", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: free-trial length (FR-27)", "3" },
                    { 5L, "Referral.RewardCreditToReferrer", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: PLACEHOLDER pending business sign-off (FR-28)", "100" },
                    { 6L, "Referral.RewardCreditToReferred", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: PLACEHOLDER pending business sign-off (FR-28)", "50" },
                    { 7L, "Referral.CreditExpiryDays", new DateTime(2026, 6, 17, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2020, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed: PLACEHOLDER pending business sign-off (FR-28)", "365" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "KycStatuses",
                columns: new[] { "KycStatusId", "Code", "DisplayName" },
                values: new object[,]
                {
                    { (byte)0, "NONE", "Not verified" },
                    { (byte)1, "PENDING", "Pending review" },
                    { (byte)2, "VERIFIED", "Verified" },
                    { (byte)3, "REJECTED", "Rejected" },
                    { (byte)4, "EXPIRED", "Expired" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "MembershipTiers",
                columns: new[] { "MembershipTierId", "AnnualPriceTHB", "Code", "DisplayName", "IsActive", "RequiresKyc" },
                values: new object[,]
                {
                    { (byte)1, 1000m, "Normal", "Normal", true, false },
                    { (byte)2, 1500m, "Verified", "Verified", true, true },
                    { (byte)3, 2000m, "Premium", "Premium", true, true }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "PromotionPackages",
                columns: new[] { "PromotionPackageId", "Code", "CreditCost", "DisplayName", "DurationDays", "IsActive", "PromotionType" },
                values: new object[,]
                {
                    { (byte)1, "FEAT_7", 100m, "Featured 7 days", 7, true, "Featured" },
                    { (byte)2, "TOP_3", 80m, "Top of list 3 days", 3, true, "TopOfList" },
                    { (byte)3, "HL_7", 50m, "Highlight 7 days", 7, true, "Highlight" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppraisalOpinions_AppraiserUserId",
                schema: "dbo",
                table: "AppraisalOpinions",
                column: "AppraiserUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Appraisal_Product",
                schema: "dbo",
                table: "AppraisalOpinions",
                column: "ProductId",
                filter: "[IsPublished] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_Status_End",
                schema: "dbo",
                table: "Auctions",
                columns: new[] { "Status", "EndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_WinningBidId",
                schema: "dbo",
                table: "Auctions",
                column: "WinningBidId");

            migrationBuilder.CreateIndex(
                name: "UX_Auctions_Product",
                schema: "dbo",
                table: "Auctions",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Actor",
                schema: "dbo",
                table: "AuditLogs",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Correlation",
                schema: "dbo",
                table: "AuditLogs",
                column: "CorrelationId",
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Entity",
                schema: "dbo",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Bids_Auction_Amount",
                schema: "dbo",
                table: "Bids",
                columns: new[] { "AuctionId", "Amount" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Bids_Bidder",
                schema: "dbo",
                table: "Bids",
                column: "BidderId");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_Shill",
                schema: "dbo",
                table: "Bids",
                column: "AuctionId",
                filter: "[IsFlaggedShill] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_CreatedByUserId",
                schema: "dbo",
                table: "BlacklistEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_ReasonCodeId",
                schema: "dbo",
                table: "BlacklistEntries",
                column: "ReasonCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntries_ReviewedByUserId",
                schema: "dbo",
                table: "BlacklistEntries",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Blacklist_User",
                schema: "dbo",
                table: "BlacklistEntries",
                column: "UserId",
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "UQ_ReasonCodes_Code",
                schema: "dbo",
                table: "BlacklistReasonCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                schema: "dbo",
                table: "Categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "UQ_Categories_Slug",
                schema: "dbo",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfigVersions_CreatedByUserId",
                schema: "dbo",
                table: "ConfigVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_ConfigVer_KeyEffective",
                schema: "dbo",
                table: "ConfigVersions",
                columns: new[] { "ConfigKey", "EffectiveFromUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Consent_User_Type",
                schema: "dbo",
                table: "ConsentRecords",
                columns: new[] { "UserId", "ConsentType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTx_Expiry",
                schema: "dbo",
                table: "CreditTransactions",
                column: "ExpiresAtUtc",
                filter: "[ExpiresAtUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTx_User",
                schema: "dbo",
                table: "CreditTransactions",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CreditTx_Idempotency",
                schema: "dbo",
                table: "CreditTransactions",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CreditTx_TypeRef",
                schema: "dbo",
                table: "CreditTransactions",
                columns: new[] { "Type", "RefId" },
                unique: true,
                filter: "[RefId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_HandledByUserId",
                schema: "dbo",
                table: "Disputes",
                column: "HandledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_RaisedByUserId",
                schema: "dbo",
                table: "Disputes",
                column: "RaisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_ReasonCodeId",
                schema: "dbo",
                table: "Disputes",
                column: "ReasonCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispute_Status",
                schema: "dbo",
                table: "Disputes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Dispute_Tx",
                schema: "dbo",
                table: "Disputes",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_FeeInvoices_RelatedMembershipId",
                schema: "dbo",
                table: "FeeInvoices",
                column: "RelatedMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_FeeInvoices_RelatedProductId",
                schema: "dbo",
                table: "FeeInvoices",
                column: "RelatedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Fee_User_Status",
                schema: "dbo",
                table: "FeeInvoices",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UQ_KycStatuses_Code",
                schema: "dbo",
                table: "KycStatuses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kyc_UserId",
                schema: "dbo",
                table: "KycVerifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KycVerifications_KycStatusId",
                schema: "dbo",
                table: "KycVerifications",
                column: "KycStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingPromotions_PromotionPackageId",
                schema: "dbo",
                table: "ListingPromotions",
                column: "PromotionPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingPromotions_UserId",
                schema: "dbo",
                table: "ListingPromotions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ListPromo_Active_End",
                schema: "dbo",
                table: "ListingPromotions",
                column: "EndsAtUtc",
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_ListPromo_Product",
                schema: "dbo",
                table: "ListingPromotions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "UQ_ListPromo_CreditTx",
                schema: "dbo",
                table: "ListingPromotions",
                column: "CreditTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Membership_Expiry",
                schema: "dbo",
                table: "Memberships",
                columns: new[] { "Status", "PaidThroughUtc", "TrialEndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_MembershipTierId",
                schema: "dbo",
                table: "Memberships",
                column: "MembershipTierId");

            migrationBuilder.CreateIndex(
                name: "UX_Membership_LivePerUser",
                schema: "dbo",
                table: "Memberships",
                column: "UserId",
                unique: true,
                filter: "[Status] IN ('Trial','Active')");

            migrationBuilder.CreateIndex(
                name: "UQ_Tiers_Code",
                schema: "dbo",
                table: "MembershipTiers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notif_Due",
                schema: "dbo",
                table: "Notifications",
                column: "ScheduledForUtc",
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedMembershipId",
                schema: "dbo",
                table: "Notifications",
                column: "RelatedMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Notif_User",
                schema: "dbo",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Notif_NoDup",
                schema: "dbo",
                table: "Notifications",
                columns: new[] { "UserId", "Type", "RelatedMembershipId", "Milestone" },
                unique: true,
                filter: "[RelatedMembershipId] IS NOT NULL AND [Milestone] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyActions_IssuedByUserId",
                schema: "dbo",
                table: "PenaltyActions",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyActions_ReasonCodeId",
                schema: "dbo",
                table: "PenaltyActions",
                column: "ReasonCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyActions_RelatedTransactionId",
                schema: "dbo",
                table: "PenaltyActions",
                column: "RelatedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Penalty_User",
                schema: "dbo",
                table: "PenaltyActions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_Product",
                schema: "dbo",
                table: "ProductImages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Category_Status",
                schema: "dbo",
                table: "Products",
                columns: new[] { "CategoryId", "Status" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Rarity_Status",
                schema: "dbo",
                table: "Products",
                columns: new[] { "Rarity", "Status" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Seller",
                schema: "dbo",
                table: "Products",
                column: "SellerId",
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UQ_PromoPkg_Code",
                schema: "dbo",
                table: "PromotionPackages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_RefCode_Code",
                schema: "dbo",
                table: "ReferralCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_RefCode_User",
                schema: "dbo",
                table: "ReferralCodes",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referral_Referrer",
                schema: "dbo",
                table: "Referrals",
                column: "ReferrerUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_Referral_Referred",
                schema: "dbo",
                table: "Referrals",
                column: "ReferredUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_Reviewee",
                schema: "dbo",
                table: "Reviews",
                column: "RevieweeId",
                filter: "[IsHidden] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ReviewerId",
                schema: "dbo",
                table: "Reviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "UQ_Reviews_OncePerTx",
                schema: "dbo",
                table: "Reviews",
                columns: new[] { "TransactionId", "ReviewerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProductId",
                schema: "dbo",
                table: "Transactions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WinningBidId",
                schema: "dbo",
                table: "Transactions",
                column: "WinningBidId");

            migrationBuilder.CreateIndex(
                name: "IX_Tx_Buyer",
                schema: "dbo",
                table: "Transactions",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_Tx_Seller",
                schema: "dbo",
                table: "Transactions",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_Tx_Status",
                schema: "dbo",
                table: "Transactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionStatusHistory_ChangedByUserId",
                schema: "dbo",
                table: "TransactionStatusHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TxHist_Tx",
                schema: "dbo",
                table: "TransactionStatusHistory",
                columns: new[] { "TransactionId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustScoreHistory_ReasonCodeId",
                schema: "dbo",
                table: "TrustScoreHistory",
                column: "ReasonCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustScoreHistory_RelatedTransactionId",
                schema: "dbo",
                table: "TrustScoreHistory",
                column: "RelatedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TSHist_User",
                schema: "dbo",
                table: "TrustScoreHistory",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Users_NormalizedEmail",
                schema: "dbo",
                table: "Users",
                column: "NormalizedEmail",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Auctions_WinningBid",
                schema: "dbo",
                table: "Auctions",
                column: "WinningBidId",
                principalSchema: "dbo",
                principalTable: "Bids",
                principalColumn: "BidId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Auctions_Product",
                schema: "dbo",
                table: "Auctions");

            migrationBuilder.DropForeignKey(
                name: "FK_Bids_Bidder",
                schema: "dbo",
                table: "Bids");

            migrationBuilder.DropForeignKey(
                name: "FK_Auctions_WinningBid",
                schema: "dbo",
                table: "Auctions");

            migrationBuilder.DropTable(
                name: "AppraisalOpinions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BlacklistEntries",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ConfigVersions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ConsentRecords",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CreditAccounts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Disputes",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FeeInvoices",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "KycSensitiveData",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ListingPromotions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PenaltyActions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ProductImages",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "ReferralCodes",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Referrals",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Reviews",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TransactionStatusHistory",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TrustScoreHistory",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "TrustScores",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "UserProfiles",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "KycVerifications",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CreditTransactions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "PromotionPackages",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Memberships",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BlacklistReasonCodes",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Transactions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "KycStatuses",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "MembershipTiers",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Categories",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Bids",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Auctions",
                schema: "dbo");
        }
    }
}
