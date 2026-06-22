using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class TrustScoreConfiguration : IEntityTypeConfiguration<TrustScore>
{
    public void Configure(EntityTypeBuilder<TrustScore> b)
    {
        b.ToTable("TrustScores", t =>
            t.HasCheckConstraint("CK_TrustScore_Range", "[Score] BETWEEN 0 AND 100"));
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.Score).HasDefaultValue(100);
        b.Property(x => x.LastCalculatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.User).WithOne(x => x.TrustScore)
            .HasForeignKey<TrustScore>(x => x.UserId).HasConstraintName("FK_TrustScores_Users").OnDelete(DeleteBehavior.Cascade);
    }
}

public class TrustScoreHistoryConfiguration : IEntityTypeConfiguration<TrustScoreHistory>
{
    public void Configure(EntityTypeBuilder<TrustScoreHistory> b)
    {
        b.ToTable("TrustScoreHistory");
        b.HasKey(x => x.TrustScoreHistoryId);
        b.Property(x => x.TrustScoreHistoryId).UseIdentityColumn();
        b.Property(x => x.Note).HasMaxLength(400);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc }).HasDatabaseName("IX_TSHist_User");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_TSHist_User").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ReasonCode).WithMany()
            .HasForeignKey(x => x.ReasonCodeId).HasConstraintName("FK_TSHist_Reason").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.RelatedTransaction).WithMany()
            .HasForeignKey(x => x.RelatedTransactionId).HasConstraintName("FK_TSHist_Tx").OnDelete(DeleteBehavior.NoAction);
    }
}

public class PenaltyActionConfiguration : IEntityTypeConfiguration<PenaltyAction>
{
    public void Configure(EntityTypeBuilder<PenaltyAction> b)
    {
        b.ToTable("PenaltyActions", t =>
            t.HasCheckConstraint("CK_Penalty_Type", "[ActionType] IN ('Warning','ScoreDeduct','Suspend','Ban')"));
        b.HasKey(x => x.PenaltyActionId);
        b.Property(x => x.PenaltyActionId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.ActionType).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.EffectiveAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_Penalty_User");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Penalty_User").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ReasonCode).WithMany()
            .HasForeignKey(x => x.ReasonCodeId).HasConstraintName("FK_Penalty_Reason").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.RelatedTransaction).WithMany()
            .HasForeignKey(x => x.RelatedTransactionId).HasConstraintName("FK_Penalty_Tx").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.IssuedByUser).WithMany()
            .HasForeignKey(x => x.IssuedByUserId).HasConstraintName("FK_Penalty_IssuedBy").OnDelete(DeleteBehavior.NoAction);
    }
}

public class BlacklistEntryConfiguration : IEntityTypeConfiguration<BlacklistEntry>
{
    public void Configure(EntityTypeBuilder<BlacklistEntry> b)
    {
        // Internal warn-on-deal register with due process (FR-17): reason codes, ReviewStatus/AppealStatus/ExpiresAt.
        b.ToTable("BlacklistEntries", t =>
        {
            t.HasCheckConstraint("CK_Blacklist_Review", "[ReviewStatus] IN ('PendingReview','Confirmed','Rejected','Appealed','Overturned')");
            t.HasCheckConstraint("CK_Blacklist_Appeal", "[AppealStatus] IN ('None','Requested','UnderReview','Accepted','Denied')");
        });
        b.HasKey(x => x.BlacklistEntryId);
        b.Property(x => x.BlacklistEntryId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.InternalNote).HasMaxLength(500);
        b.Property(x => x.ReviewStatus).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.BlacklistReviewStatus.PendingReview);
        b.Property(x => x.AppealStatus).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.BlacklistAppealStatus.None);
        b.Property(x => x.AppealNote).HasMaxLength(500);
        b.Property(x => x.EffectiveAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.UserId).HasFilter("[IsActive] = 1").HasDatabaseName("IX_Blacklist_User");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Blacklist_User").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ReasonCode).WithMany()
            .HasForeignKey(x => x.ReasonCodeId).HasConstraintName("FK_Blacklist_Reason").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).HasConstraintName("FK_Blacklist_CreatedBy").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ReviewedByUser).WithMany()
            .HasForeignKey(x => x.ReviewedByUserId).HasConstraintName("FK_Blacklist_ReviewedBy").OnDelete(DeleteBehavior.NoAction);
    }
}
