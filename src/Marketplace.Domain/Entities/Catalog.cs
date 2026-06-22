using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Entities;

// ============================ 5. PRODUCTS / LISTINGS ======================

/// <summary>dbo.Products — listing. FixedPrice is asking price only; platform never holds money.</summary>
public class Product
{
    public Guid ProductId { get; set; }
    public Guid SellerId { get; set; }
    public int CategoryId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string ConditionGrade { get; set; } = "Used";
    /// <summary>Rarity slug — common/rare/epic/legendary (CHECK constrained). Drives the catalog card border + Explore rarity filter (FR-08).</summary>
    public string Rarity { get; set; } = "common";
    /// <summary>Optional serial label e.g. "#0042/0050". Display-only; not a guarantee (FR-07).</summary>
    public string? SerialLabel { get; set; }
    public ListingType ListingType { get; set; } = ListingType.FixedPrice;
    public decimal? FixedPrice { get; set; }       // CHECK NULL OR >= 0
    public string Currency { get; set; } = "THB";
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public User Seller { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Auction? Auction { get; set; }
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ListingPromotion> Promotions { get; set; } = new List<ListingPromotion>();
}

/// <summary>dbo.ProductImages — ON DELETE CASCADE from Product.</summary>
public class ProductImage
{
    public Guid ProductImageId { get; set; }
    public Guid ProductId { get; set; }
    public string Url { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Product Product { get; set; } = null!;
}

// ============================ 6. AUCTIONS / BIDS ==========================

/// <summary>dbo.Auctions — 1:1 with Product. WinningBidId FK added separately (avoids circular FK).</summary>
public class Auction
{
    public Guid AuctionId { get; set; }
    public Guid ProductId { get; set; }
    public decimal StartingPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public decimal BidIncrement { get; set; } = 1m;
    public decimal? CurrentHighBid { get; set; }
    public Guid? WinningBidId { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }          // CHECK EndAtUtc > StartAtUtc
    public AuctionStatus Status { get; set; } = AuctionStatus.Scheduled;
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    public Product Product { get; set; } = null!;
    public Bid? WinningBid { get; set; }
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}

/// <summary>dbo.Bids — includes anti-shill fields stored as hashes (Legal #6, FR-11).</summary>
public class Bid
{
    public Guid BidId { get; set; }
    public Guid AuctionId { get; set; }
    public Guid BidderId { get; set; }
    public decimal Amount { get; set; }            // CHECK > 0
    public BidStatus Status { get; set; } = BidStatus.Active;
    public byte[]? IpAddressHash { get; set; }
    public byte[]? DeviceFingerprintHash { get; set; }
    public bool IsFlaggedShill { get; set; }
    public string? RelationshipFlag { get; set; }  // SAME_DEVICE/SAME_IP/LINKED_ACCOUNT
    public DateTime PlacedAtUtc { get; set; }

    public Auction Auction { get; set; } = null!;
    public User Bidder { get; set; } = null!;
}

// ============================ 18. APPRAISAL OPINIONS (B-03 / G-3) ==========

/// <summary>
/// dbo.AppraisalOpinions — independent appraiser opinion on a product (FR-07 / IS-8).
/// LEGAL #2: the platform does NOT warrant authenticity. Each row is an OPINION only and
/// stores the DisclaimerVersion shown at the time so we can prove which disclaimer applied
/// (UI must avoid "รับประกัน/ของแท้ 100%").
/// </summary>
public class AppraisalOpinion
{
    public Guid AppraisalOpinionId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? AppraiserUserId { get; set; }     // platform user (FK) and/or external named expert
    public string AppraiserName { get; set; } = null!;
    public string OpinionText { get; set; } = null!;
    public string DisclaimerVersion { get; set; } = null!;
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Product Product { get; set; } = null!;
    public User? AppraiserUser { get; set; }
}
