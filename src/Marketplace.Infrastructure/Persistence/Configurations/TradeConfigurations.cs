using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        // No-touch: status flow has no Held/Escrow/Refunded; no balance/wallet columns exist.
        b.ToTable("Transactions", t =>
        {
            t.HasCheckConstraint("CK_Tx_Status", "[Status] IN ('Pending','Transferred','Confirmed','Disputed','Cancelled')");
            t.HasCheckConstraint("CK_Tx_Amount", "[AgreedAmount] >= 0");
            t.HasCheckConstraint("CK_Tx_Parties", "[BuyerId] <> [SellerId]");
        });
        b.HasKey(x => x.TransactionId);
        b.Property(x => x.TransactionId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.AgreedAmount).HasColumnType("decimal(12,2)");
        b.Property(x => x.Currency).HasColumnType("char(3)").HasDefaultValue("THB");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.TransactionStatus.Pending);
        b.Property(x => x.ExternalPaymentNote).HasMaxLength(400);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.TransferredAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ConfirmedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.SellerId).HasDatabaseName("IX_Tx_Seller");
        b.HasIndex(x => x.BuyerId).HasDatabaseName("IX_Tx_Buyer");
        b.HasIndex(x => x.Status).HasDatabaseName("IX_Tx_Status");
        b.HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).HasConstraintName("FK_Tx_Product").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Seller).WithMany()
            .HasForeignKey(x => x.SellerId).HasConstraintName("FK_Tx_Seller").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Buyer).WithMany()
            .HasForeignKey(x => x.BuyerId).HasConstraintName("FK_Tx_Buyer").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.WinningBid).WithMany()
            .HasForeignKey(x => x.WinningBidId).HasConstraintName("FK_Tx_WinningBid").OnDelete(DeleteBehavior.NoAction);
    }
}

public class TransactionStatusHistoryConfiguration : IEntityTypeConfiguration<TransactionStatusHistory>
{
    public void Configure(EntityTypeBuilder<TransactionStatusHistory> b)
    {
        b.ToTable("TransactionStatusHistory");
        b.HasKey(x => x.TransactionStatusHistoryId);
        b.Property(x => x.TransactionStatusHistoryId).UseIdentityColumn();
        b.Property(x => x.FromStatus).HasColumnType("varchar(20)");
        b.Property(x => x.ToStatus).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.Note).HasMaxLength(400);
        b.Property(x => x.ChangedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.TransactionId, x.ChangedAtUtc }).HasDatabaseName("IX_TxHist_Tx");
        b.HasOne(x => x.Transaction).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.TransactionId).HasConstraintName("FK_TxHist_Tx").OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ChangedByUser).WithMany()
            .HasForeignKey(x => x.ChangedByUserId).HasConstraintName("FK_TxHist_User").OnDelete(DeleteBehavior.NoAction);
    }
}

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("Reviews", t =>
        {
            t.HasCheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Reviews_Parties", "[ReviewerId] <> [RevieweeId]");
        });
        b.HasKey(x => x.ReviewId);
        b.Property(x => x.ReviewId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.TransactionId, x.ReviewerId }).IsUnique().HasDatabaseName("UQ_Reviews_OncePerTx");
        b.HasIndex(x => x.RevieweeId).HasFilter("[IsHidden] = 0").HasDatabaseName("IX_Reviews_Reviewee");
        b.HasOne(x => x.Transaction).WithMany(x => x.Reviews)
            .HasForeignKey(x => x.TransactionId).HasConstraintName("FK_Reviews_Tx").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Reviewer).WithMany()
            .HasForeignKey(x => x.ReviewerId).HasConstraintName("FK_Reviews_Reviewer").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Reviewee).WithMany()
            .HasForeignKey(x => x.RevieweeId).HasConstraintName("FK_Reviews_Reviewee").OnDelete(DeleteBehavior.NoAction);
    }
}

public class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> b)
    {
        b.ToTable("Disputes", t =>
            t.HasCheckConstraint("CK_Dispute_Status", "[Status] IN ('Open','UnderReview','Resolved','Rejected','Escalated')"));
        b.HasKey(x => x.DisputeId);
        b.Property(x => x.DisputeId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.DisputeStatus.Open);
        b.Property(x => x.Resolution).HasMaxLength(2000);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.ResolvedAtUtc).HasColumnType("datetime2(3)");
        b.HasIndex(x => x.TransactionId).HasDatabaseName("IX_Dispute_Tx");
        b.HasIndex(x => x.Status).HasDatabaseName("IX_Dispute_Status");
        b.HasOne(x => x.Transaction).WithMany(x => x.Disputes)
            .HasForeignKey(x => x.TransactionId).HasConstraintName("FK_Dispute_Tx").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RaisedByUser).WithMany()
            .HasForeignKey(x => x.RaisedByUserId).HasConstraintName("FK_Dispute_RaisedBy").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ReasonCode).WithMany()
            .HasForeignKey(x => x.ReasonCodeId).HasConstraintName("FK_Dispute_Reason").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.HandledByUser).WithMany()
            .HasForeignKey(x => x.HandledByUserId).HasConstraintName("FK_Dispute_HandledBy").OnDelete(DeleteBehavior.NoAction);
    }
}
