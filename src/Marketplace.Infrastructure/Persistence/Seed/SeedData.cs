using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>
/// Lookup seed data mirroring the INSERTs in sql/schema.sql.
/// Applied via HasData() so InitialCreate migration carries the rows.
/// </summary>
internal static class SeedData
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        // KYC statuses
        modelBuilder.Entity<KycStatus>().HasData(
            new KycStatus { KycStatusId = 0, Code = "NONE",     DisplayName = "Not verified" },
            new KycStatus { KycStatusId = 1, Code = "PENDING",  DisplayName = "Pending review" },
            new KycStatus { KycStatusId = 2, Code = "VERIFIED", DisplayName = "Verified" },
            new KycStatus { KycStatusId = 3, Code = "REJECTED", DisplayName = "Rejected" },
            new KycStatus { KycStatusId = 4, Code = "EXPIRED",  DisplayName = "Expired" },
            // M2 (NDID e-KYC): provider redirect started, awaiting async callback/IAL result.
            new KycStatus { KycStatusId = 5, Code = "INITIATED", DisplayName = "Initiated (awaiting provider)" });

        // Membership tiers — annual price Normal=1000 / Verified=1500 / Premium=2000 (FR-27).
        modelBuilder.Entity<MembershipTier>().HasData(
            new MembershipTier { MembershipTierId = 1, Code = "Normal",   DisplayName = "Normal",   RequiresKyc = false, MonthlyFee = 0m, AnnualPriceTHB = 1000m, IsActive = true },
            new MembershipTier { MembershipTierId = 2, Code = "Verified", DisplayName = "Verified", RequiresKyc = true,  MonthlyFee = 0m, AnnualPriceTHB = 1500m, IsActive = true },
            new MembershipTier { MembershipTierId = 3, Code = "Premium",  DisplayName = "Premium",  RequiresKyc = true,  MonthlyFee = 0m, AnnualPriceTHB = 2000m, IsActive = true });

        // Promotion packages (credit-priced).
        modelBuilder.Entity<PromotionPackage>().HasData(
            new PromotionPackage { PromotionPackageId = 1, Code = "FEAT_7", DisplayName = "Featured 7 days",    PromotionType = PromotionType.Featured,  DurationDays = 7, CreditCost = 100m, IsActive = true },
            new PromotionPackage { PromotionPackageId = 2, Code = "TOP_3",  DisplayName = "Top of list 3 days", PromotionType = PromotionType.TopOfList, DurationDays = 3, CreditCost = 80m,  IsActive = true },
            new PromotionPackage { PromotionPackageId = 3, Code = "HL_7",   DisplayName = "Highlight 7 days",   PromotionType = PromotionType.Highlight, DurationDays = 7, CreditCost = 50m,  IsActive = true });

        // Blacklist / reason codes — standard codes (Legal #3).
        modelBuilder.Entity<BlacklistReasonCode>().HasData(
            new BlacklistReasonCode { ReasonCodeId = 1, Code = "LATE_SHIPMENT",    DisplayName = "Late shipment",         Severity = 1, DefaultScorePenalty = -20,  IsActive = true },
            new BlacklistReasonCode { ReasonCodeId = 2, Code = "NO_RESPONSE",      DisplayName = "Unresponsive",          Severity = 1, DefaultScorePenalty = -20,  IsActive = true },
            new BlacklistReasonCode { ReasonCodeId = 3, Code = "ITEM_NOT_AS_DESC", DisplayName = "Item not as described", Severity = 2, DefaultScorePenalty = -20,  IsActive = true },
            new BlacklistReasonCode { ReasonCodeId = 4, Code = "SHILL_BIDDING",    DisplayName = "Shill bidding",         Severity = 3, DefaultScorePenalty = -40,  IsActive = true },
            new BlacklistReasonCode { ReasonCodeId = 5, Code = "FAKE_ITEM",        DisplayName = "Counterfeit item",      Severity = 4, DefaultScorePenalty = -60,  IsActive = true },
            new BlacklistReasonCode { ReasonCodeId = 6, Code = "FRAUD_PAYMENT",    DisplayName = "Payment fraud",         Severity = 5, DefaultScorePenalty = -100, IsActive = true },
            // FR-19/FR-14: positive trust reward applied by TradeService.ConfirmReceiptAsync on a clean
            // completed deal. ReasonCodeId 7 is the id that TradeService.TryRewardCompletedDealAsync expects;
            // before this row existed it failed soft (skipped the reward). Severity 1, +5 score.
            new BlacklistReasonCode { ReasonCodeId = 7, Code = "COMPLETED_DEAL",   DisplayName = "Completed deal",        Severity = 1, DefaultScorePenalty = 5,    IsActive = true });

        // Categories (root examples for collectibles marketplace).
        modelBuilder.Entity<Category>().HasData(
            new Category { CategoryId = 1, ParentCategoryId = null, Name = "Coins & Banknotes", Slug = "coins-banknotes", IsActive = true, CreatedAtUtc = SeedTimestamp },
            new Category { CategoryId = 2, ParentCategoryId = null, Name = "Stamps",            Slug = "stamps",          IsActive = true, CreatedAtUtc = SeedTimestamp },
            new Category { CategoryId = 3, ParentCategoryId = null, Name = "Trading Cards",     Slug = "trading-cards",   IsActive = true, CreatedAtUtc = SeedTimestamp },
            new Category { CategoryId = 4, ParentCategoryId = null, Name = "Amulets",           Slug = "amulets",         IsActive = true, CreatedAtUtc = SeedTimestamp },
            new Category { CategoryId = 5, ParentCategoryId = null, Name = "Figures & Toys",    Slug = "figures-toys",    IsActive = true, CreatedAtUtc = SeedTimestamp });

        // ConfigVersions baseline (B-04/G-4, FR-31/FR-35) — the admin-editable config history that the
        // growth/membership services read through ConfigVersionResolver instead of hardcoding. Without
        // these rows the services silently fall back to in-code defaults; seeding them makes the resolver
        // the source of truth from day one. Values are string-encoded (resolver parses decimal/int).
        // EffectiveFromUtc/CreatedAtUtc are the constant SeedTimestamp so the migration stays deterministic
        // (HasData forbids SYSUTCDATETIME()). ConfigKey strings mirror ConfigKeys.* (do not drift).
        modelBuilder.Entity<ConfigVersion>().HasData(
            // FR-27: annual membership price per tier — must match MembershipTiers + sql/schema.sql (1000/1500/2000 THB).
            new ConfigVersion { ConfigVersionId = 1, ConfigKey = "MembershipTier.Normal.AnnualPriceTHB",   Value = "1000", EffectiveFromUtc = ConfigBaselineEffective, CreatedByUserId = null, Note = "seed: baseline annual price (FR-27)", CreatedAtUtc = SeedTimestamp },
            new ConfigVersion { ConfigVersionId = 2, ConfigKey = "MembershipTier.Verified.AnnualPriceTHB", Value = "1500", EffectiveFromUtc = ConfigBaselineEffective, CreatedByUserId = null, Note = "seed: baseline annual price (FR-27)", CreatedAtUtc = SeedTimestamp },
            new ConfigVersion { ConfigVersionId = 3, ConfigKey = "MembershipTier.Premium.AnnualPriceTHB",  Value = "2000", EffectiveFromUtc = ConfigBaselineEffective, CreatedByUserId = null, Note = "seed: baseline annual price (FR-27)", CreatedAtUtc = SeedTimestamp },
            // FR-27: free-trial length in months (3 per SRS/memory).
            new ConfigVersion { ConfigVersionId = 4, ConfigKey = "Membership.TrialMonths",                 Value = "3",    EffectiveFromUtc = ConfigBaselineEffective, CreatedByUserId = null, Note = "seed: free-trial length (FR-27)",     CreatedAtUtc = SeedTimestamp },
            // FR-28: single-level referral reward credit + credit expiry window.
            // NOTE(business): reward amounts and expiry are placeholders pending business sign-off — no
            // authoritative value exists in schema/SRS yet. Adjust via a new ConfigVersion row when confirmed.
            new ConfigVersion { ConfigVersionId = 5, ConfigKey = "Referral.RewardCreditToReferrer",        Value = "100",  EffectiveFromUtc = ConfigBaselineEffective, CreatedByUserId = null, Note = "seed: PLACEHOLDER pending business sign-off (FR-28)", CreatedAtUtc = SeedTimestamp },
            new ConfigVersion { ConfigVersionId = 6, ConfigKey = "Referral.RewardCreditToReferred",        Value = "50",   EffectiveFromUtc = ConfigBaselineEffective, CreatedByUserId = null, Note = "seed: PLACEHOLDER pending business sign-off (FR-28)", CreatedAtUtc = SeedTimestamp },
            new ConfigVersion { ConfigVersionId = 7, ConfigKey = "Referral.CreditExpiryDays",              Value = "365",  EffectiveFromUtc = ConfigBaselineEffective, CreatedByUserId = null, Note = "seed: PLACEHOLDER pending business sign-off (FR-28)", CreatedAtUtc = SeedTimestamp });
    }

    // Static timestamp so migrations stay deterministic (HasData requires a constant, not SYSUTCDATETIME()).
    private static readonly DateTime SeedTimestamp = new(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc);

    // Baseline config is "effective since the platform epoch" so that ANY later admin change (with a more
    // recent EffectiveFromUtc) always wins in ConfigVersionResolver. Dating the baseline to "now" would make
    // it beat genuine overrides whose effective date is only slightly in the past.
    private static readonly DateTime ConfigBaselineEffective = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
