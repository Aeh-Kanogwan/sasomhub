using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Products_Type", "[ListingType] IN ('FixedPrice','Auction')");
            t.HasCheckConstraint("CK_Products_Status", "[Status] IN ('Draft','Active','Sold','Closed','Removed')");
            t.HasCheckConstraint("CK_Products_FixedPrice", "[FixedPrice] IS NULL OR [FixedPrice] >= 0");
            t.HasCheckConstraint("CK_Products_Rarity", "[Rarity] IN ('common','rare','epic','legendary')");
        });
        b.HasKey(x => x.ProductId);
        b.Property(x => x.ProductId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.ConditionGrade).HasColumnType("varchar(20)").HasDefaultValue("Used");
        b.Property(x => x.Rarity).HasColumnType("varchar(20)").HasDefaultValue("common");
        b.Property(x => x.SerialLabel).HasMaxLength(40);
        b.Property(x => x.ListingType).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.ListingType.FixedPrice);
        b.Property(x => x.FixedPrice).HasColumnType("decimal(12,2)");
        b.Property(x => x.Currency).HasColumnType("char(3)").HasDefaultValue("THB");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.ProductStatus.Draft);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.SellerId).HasFilter("[IsDeleted] = 0").HasDatabaseName("IX_Products_Seller");
        b.HasIndex(x => new { x.CategoryId, x.Status }).HasFilter("[IsDeleted] = 0").HasDatabaseName("IX_Products_Category_Status");
        // FR-08: Explore filters Active listings by Rarity; supports the rarity sidebar facet.
        b.HasIndex(x => new { x.Rarity, x.Status }).HasFilter("[IsDeleted] = 0").HasDatabaseName("IX_Products_Rarity_Status");
        b.HasOne(x => x.Seller).WithMany(x => x.Products)
            .HasForeignKey(x => x.SellerId).HasConstraintName("FK_Products_Seller").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Category).WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId).HasConstraintName("FK_Products_Category").OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("ProductImages");
        b.HasKey(x => x.ProductImageId);
        b.Property(x => x.ProductImageId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Url).HasMaxLength(512).IsRequired();
        b.Property(x => x.SortOrder).HasDefaultValue(0);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.ProductId).HasDatabaseName("IX_ProductImages_Product");
        b.HasOne(x => x.Product).WithMany(x => x.Images)
            .HasForeignKey(x => x.ProductId).HasConstraintName("FK_ProductImages_Product").OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuctionConfiguration : IEntityTypeConfiguration<Auction>
{
    public void Configure(EntityTypeBuilder<Auction> b)
    {
        b.ToTable("Auctions", t =>
        {
            t.HasCheckConstraint("CK_Auctions_Status", "[Status] IN ('Scheduled','Open','Closed','Cancelled')");
            t.HasCheckConstraint("CK_Auctions_Time", "[EndAtUtc] > [StartAtUtc]");
            t.HasCheckConstraint("CK_Auctions_Prices",
                "[StartingPrice] >= 0 AND ([ReservePrice] IS NULL OR [ReservePrice] >= [StartingPrice])");
        });
        b.HasKey(x => x.AuctionId);
        b.Property(x => x.AuctionId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.StartingPrice).HasColumnType("decimal(12,2)");
        b.Property(x => x.ReservePrice).HasColumnType("decimal(12,2)");
        b.Property(x => x.BidIncrement).HasColumnType("decimal(12,2)").HasDefaultValue(1m);
        b.Property(x => x.CurrentHighBid).HasColumnType("decimal(12,2)");
        b.Property(x => x.StartAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.EndAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.AuctionStatus.Scheduled);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.ProductId).IsUnique().HasDatabaseName("UX_Auctions_Product");
        b.HasIndex(x => new { x.Status, x.EndAtUtc }).HasDatabaseName("IX_Auctions_Status_End");
        b.HasOne(x => x.Product).WithOne(x => x.Auction)
            .HasForeignKey<Auction>(x => x.ProductId).HasConstraintName("FK_Auctions_Product").OnDelete(DeleteBehavior.Restrict);
        // WinningBid FK added separately to avoid circular dependency (FK_Auctions_WinningBid).
        b.HasOne(x => x.WinningBid).WithMany()
            .HasForeignKey(x => x.WinningBidId).HasConstraintName("FK_Auctions_WinningBid").OnDelete(DeleteBehavior.NoAction);
    }
}

public class BidConfiguration : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> b)
    {
        b.ToTable("Bids", t =>
        {
            t.HasCheckConstraint("CK_Bids_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_Bids_Status", "[Status] IN ('Active','Outbid','Won','Retracted','Voided')");
        });
        b.HasKey(x => x.BidId);
        b.Property(x => x.BidId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Amount).HasColumnType("decimal(12,2)");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.BidStatus.Active);
        b.Property(x => x.IpAddressHash).HasColumnType("varbinary(32)");
        b.Property(x => x.DeviceFingerprintHash).HasColumnType("varbinary(32)");
        b.Property(x => x.RelationshipFlag).HasColumnType("varchar(30)");
        b.Property(x => x.PlacedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.AuctionId, x.Amount }).HasDatabaseName("IX_Bids_Auction_Amount")
            .IsDescending(false, true);
        b.HasIndex(x => x.BidderId).HasDatabaseName("IX_Bids_Bidder");
        b.HasIndex(x => x.AuctionId).HasFilter("[IsFlaggedShill] = 1").HasDatabaseName("IX_Bids_Shill");
        b.HasOne(x => x.Auction).WithMany(x => x.Bids)
            .HasForeignKey(x => x.AuctionId).HasConstraintName("FK_Bids_Auction").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Bidder).WithMany(x => x.Bids)
            .HasForeignKey(x => x.BidderId).HasConstraintName("FK_Bids_Bidder").OnDelete(DeleteBehavior.Restrict);
    }
}
