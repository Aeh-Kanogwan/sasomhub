namespace Marketplace.Domain.Entities;

// ============================ 1. LOOKUP / ENUM TABLES =====================

/// <summary>dbo.KycStatuses — seed: NONE/PENDING/VERIFIED/REJECTED/EXPIRED.</summary>
public class KycStatus
{
    public byte KycStatusId { get; set; }
    public string Code { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    public ICollection<KycVerification> KycVerifications { get; set; } = new List<KycVerification>();
}

/// <summary>dbo.BlacklistReasonCodes — standard reason codes shared by Blacklist/Penalty/TrustScore/Dispute (Legal #3: code instead of free text).</summary>
public class BlacklistReasonCode
{
    public int ReasonCodeId { get; set; }
    public string Code { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public byte Severity { get; set; }            // CHECK BETWEEN 1 AND 5
    public int DefaultScorePenalty { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>dbo.Categories — self-referencing category tree.</summary>
public class Category
{
    public int CategoryId { get; set; }
    public int? ParentCategoryId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

/// <summary>dbo.MembershipTiers — annual price Normal=1000 / Verified=1500 / Premium=2000 (FR-27).</summary>
public class MembershipTier
{
    public byte MembershipTierId { get; set; }
    public string Code { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool RequiresKyc { get; set; }
    public decimal MonthlyFee { get; set; }
    public decimal AnnualPriceTHB { get; set; }   // CHECK >= 0
    public bool IsActive { get; set; } = true;

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}

/// <summary>dbo.PromotionPackages — promotion packages priced in platform credit (FR-30).</summary>
public class PromotionPackage
{
    public byte PromotionPackageId { get; set; }
    public string Code { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public Enums.PromotionType PromotionType { get; set; }
    public int DurationDays { get; set; }         // CHECK > 0
    public decimal CreditCost { get; set; }       // CHECK >= 0
    public bool IsActive { get; set; } = true;

    public ICollection<ListingPromotion> ListingPromotions { get; set; } = new List<ListingPromotion>();
}
