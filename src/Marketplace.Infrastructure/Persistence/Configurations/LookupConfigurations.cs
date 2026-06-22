using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class KycStatusConfiguration : IEntityTypeConfiguration<KycStatus>
{
    public void Configure(EntityTypeBuilder<KycStatus> b)
    {
        b.ToTable("KycStatuses");
        b.HasKey(x => x.KycStatusId);
        b.Property(x => x.KycStatusId).ValueGeneratedNever();
        b.Property(x => x.Code).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_KycStatuses_Code");
    }
}

public class BlacklistReasonCodeConfiguration : IEntityTypeConfiguration<BlacklistReasonCode>
{
    public void Configure(EntityTypeBuilder<BlacklistReasonCode> b)
    {
        b.ToTable("BlacklistReasonCodes", t =>
            t.HasCheckConstraint("CK_ReasonCodes_Severity", "[Severity] BETWEEN 1 AND 5"));
        b.HasKey(x => x.ReasonCodeId);
        b.Property(x => x.ReasonCodeId).ValueGeneratedNever();
        b.Property(x => x.Code).HasColumnType("varchar(40)").IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        b.Property(x => x.DefaultScorePenalty).HasDefaultValue(0);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_ReasonCodes_Code");
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("Categories");
        b.HasKey(x => x.CategoryId);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Slug).HasColumnType("varchar(140)").IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("UQ_Categories_Slug");
        b.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentCategoryId)
            .HasConstraintName("FK_Categories_Parent")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class MembershipTierConfiguration : IEntityTypeConfiguration<MembershipTier>
{
    public void Configure(EntityTypeBuilder<MembershipTier> b)
    {
        b.ToTable("MembershipTiers", t =>
            t.HasCheckConstraint("CK_Tier_AnnualPrice", "[AnnualPriceTHB] >= 0"));
        b.HasKey(x => x.MembershipTierId);
        b.Property(x => x.MembershipTierId).ValueGeneratedNever();
        b.Property(x => x.Code).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(50).IsRequired();
        b.Property(x => x.MonthlyFee).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
        b.Property(x => x.AnnualPriceTHB).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_Tiers_Code");
    }
}

public class PromotionPackageConfiguration : IEntityTypeConfiguration<PromotionPackage>
{
    public void Configure(EntityTypeBuilder<PromotionPackage> b)
    {
        b.ToTable("PromotionPackages", t =>
        {
            t.HasCheckConstraint("CK_PromoPkg_Type", "[PromotionType] IN ('Featured','TopOfList','Highlight')");
            t.HasCheckConstraint("CK_PromoPkg_Duration", "[DurationDays] > 0");
            t.HasCheckConstraint("CK_PromoPkg_Cost", "[CreditCost] >= 0");
        });
        b.HasKey(x => x.PromotionPackageId);
        b.Property(x => x.PromotionPackageId).ValueGeneratedNever();
        b.Property(x => x.Code).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(60).IsRequired();
        b.Property(x => x.PromotionType).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.CreditCost).HasColumnType("decimal(12,2)");
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_PromoPkg_Code");
    }
}
