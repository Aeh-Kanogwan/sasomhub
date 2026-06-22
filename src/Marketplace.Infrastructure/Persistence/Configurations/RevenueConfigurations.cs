using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class FeeInvoiceConfiguration : IEntityTypeConfiguration<FeeInvoice>
{
    public void Configure(EntityTypeBuilder<FeeInvoice> b)
    {
        // Platform revenue (company money). Fully separate from buyer<->seller flow (FR-22, no commission %).
        b.ToTable("FeeInvoices", t =>
        {
            t.HasCheckConstraint("CK_Fee_Type", "[FeeType] IN ('Membership','MembershipRenewal','MembershipUpgrade','Listing','Premium','Featured')");
            t.HasCheckConstraint("CK_Fee_Status", "[Status] IN ('Issued','Paid','Void','Overdue')");
            t.HasCheckConstraint("CK_Fee_Amount", "[Amount] >= 0");
        });
        b.HasKey(x => x.FeeInvoiceId);
        b.Property(x => x.FeeInvoiceId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.FeeType).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.Amount).HasColumnType("decimal(10,2)");
        b.Property(x => x.Currency).HasColumnType("char(3)").HasDefaultValue("THB");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.FeeInvoiceStatus.Issued);
        b.Property(x => x.IssuedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.DueAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.PaidAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ExternalPaymentRef).HasColumnType("varchar(128)");
        b.HasIndex(x => new { x.UserId, x.Status }).HasDatabaseName("IX_Fee_User_Status");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Fee_User").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.RelatedMembership).WithMany()
            .HasForeignKey(x => x.RelatedMembershipId).HasConstraintName("FK_Fee_Membership").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.RelatedProduct).WithMany()
            .HasForeignKey(x => x.RelatedProductId).HasConstraintName("FK_Fee_Product").OnDelete(DeleteBehavior.NoAction);
    }
}

public class PaymentSlipConfiguration : IEntityTypeConfiguration<PaymentSlip>
{
    public void Configure(EntityTypeBuilder<PaymentSlip> b)
    {
        // Flow B: member-uploaded proof of a bank transfer that pays a company FeeInvoice (membership fee).
        // Company money — separate from buyer↔seller trade money (no-touch preserved). Admin reviews + confirms.
        b.ToTable("PaymentSlips", t =>
        {
            t.HasCheckConstraint("CK_Slip_Status", "[Status] IN ('Pending','Approved','Rejected')");
            t.HasCheckConstraint("CK_Slip_Amount", "[AmountClaimed] >= 0");
        });
        b.HasKey(x => x.PaymentSlipId);
        b.Property(x => x.PaymentSlipId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.SlipImageUrl).HasColumnType("nvarchar(512)").IsRequired();
        b.Property(x => x.AmountClaimed).HasColumnType("decimal(10,2)");
        b.Property(x => x.TransferredAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.BankRefNote).HasColumnType("nvarchar(200)");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.PaymentSlipStatus.Pending);
        b.Property(x => x.SubmittedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.ReviewedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ReviewNote).HasColumnType("nvarchar(400)");
        b.Property(x => x.RowVersion).IsRowVersion();
        // Admin queue scan: pending slips oldest-first.
        b.HasIndex(x => new { x.Status, x.SubmittedAtUtc }).HasDatabaseName("IX_Slip_Status_Submitted");
        b.HasIndex(x => x.FeeInvoiceId).HasDatabaseName("IX_Slip_Invoice");
        // Never cascade-delete a FeeInvoice/User because a slip exists (audit/revenue trail must survive).
        b.HasOne(x => x.FeeInvoice).WithMany()
            .HasForeignKey(x => x.FeeInvoiceId).HasConstraintName("FK_Slip_Invoice").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Slip_User").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ReviewedByUser).WithMany()
            .HasForeignKey(x => x.ReviewedByUserId).HasConstraintName("FK_Slip_ReviewedBy").OnDelete(DeleteBehavior.NoAction);
    }
}

public class ReferralCodeConfiguration : IEntityTypeConfiguration<ReferralCode>
{
    public void Configure(EntityTypeBuilder<ReferralCode> b)
    {
        b.ToTable("ReferralCodes");
        b.HasKey(x => x.ReferralCodeId);
        b.Property(x => x.ReferralCodeId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Code).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("UQ_RefCode_User"); // one code per user
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UQ_RefCode_Code");
        b.HasOne(x => x.User).WithOne(x => x.ReferralCode)
            .HasForeignKey<ReferralCode>(x => x.UserId).HasConstraintName("FK_RefCode_User").OnDelete(DeleteBehavior.NoAction);
    }
}

public class ReferralConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> b)
    {
        // Single-level by design (FR-28): NO ParentReferralId/UplineUserId/Level. Reward in credit only.
        b.ToTable("Referrals", t =>
        {
            t.HasCheckConstraint("CK_Referral_NotSelf", "[ReferrerUserId] <> [ReferredUserId]");
            t.HasCheckConstraint("CK_Referral_Status", "[Status] IN ('Pending','Qualified','Rewarded','Rejected')");
        });
        b.HasKey(x => x.ReferralId);
        b.Property(x => x.ReferralId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.ReferralCode).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.ReferralStatus.Pending);
        b.Property(x => x.RewardCreditToReferrer).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
        b.Property(x => x.RewardCreditToReferred).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
        b.Property(x => x.RewardedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.ReferredUserId).IsUnique().HasDatabaseName("UQ_Referral_Referred"); // referred only once
        b.HasIndex(x => x.ReferrerUserId).HasDatabaseName("IX_Referral_Referrer");
        b.HasOne(x => x.Referrer).WithMany()
            .HasForeignKey(x => x.ReferrerUserId).HasConstraintName("FK_Referral_Referrer").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Referred).WithMany()
            .HasForeignKey(x => x.ReferredUserId).HasConstraintName("FK_Referral_Referred").OnDelete(DeleteBehavior.NoAction);
    }
}

public class CreditAccountConfiguration : IEntityTypeConfiguration<CreditAccount>
{
    public void Configure(EntityTypeBuilder<CreditAccount> b)
    {
        // Non-cashable service points — NOT a money wallet. Balance maintained from ledger.
        b.ToTable("CreditAccounts", t =>
            t.HasCheckConstraint("CK_CreditAcc_Balance", "[Balance] >= 0"));
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.Balance).HasColumnType("decimal(12,2)").HasDefaultValue(0m);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.User).WithOne(x => x.CreditAccount)
            .HasForeignKey<CreditAccount>(x => x.UserId).HasConstraintName("FK_CreditAcc_User").OnDelete(DeleteBehavior.Cascade);
    }
}

public class CreditTransactionConfiguration : IEntityTypeConfiguration<CreditTransaction>
{
    public void Configure(EntityTypeBuilder<CreditTransaction> b)
    {
        // B-06/G-6: APPEND-ONLY ledger (FR-29). DB trigger TR_CreditTransactions_NoModify rejects any
        // UPDATE/DELETE at the database layer (THROW 51003) — EF must NEVER UPDATE/DELETE these rows;
        // post a correcting Adjustment/Revoke row instead. S-04: 'Revoke' = auditable clawback.
        // NO Withdraw/CashOut/Transfer type — non-cashable, non-transferable.
        b.ToTable("CreditTransactions", t =>
        {
            t.HasCheckConstraint("CK_CreditTx_Type", "[Type] IN ('ReferralReward','PromoSpend','Adjustment','Expiry','Revoke')");
            t.HasCheckConstraint("CK_CreditTx_BalanceAfter", "[BalanceAfter] >= 0");
        });
        b.HasKey(x => x.CreditTransactionId);
        b.Property(x => x.CreditTransactionId).UseIdentityColumn();
        b.Property(x => x.Amount).HasColumnType("decimal(12,2)");
        b.Property(x => x.Type).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.RefId).HasMaxLength(64);
        // B-02/G-2: idempotency guard against double-credit on retry/double-click/worker re-run.
        b.Property(x => x.IdempotencyKey).HasMaxLength(100);
        b.Property(x => x.BalanceAfter).HasColumnType("decimal(12,2)");
        b.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.Note).HasMaxLength(400);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc }).HasDatabaseName("IX_CreditTx_User");
        b.HasIndex(x => x.ExpiresAtUtc).HasFilter("[ExpiresAtUtc] IS NOT NULL").HasDatabaseName("IX_CreditTx_Expiry");
        // B-02/G-2: two-layer double-credit protection.
        //   (1) explicit IdempotencyKey unique when supplied;
        //   (2) (Type, RefId) unique when RefId supplied — one ledger row per source entity per type.
        b.HasIndex(x => x.IdempotencyKey).IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL").HasDatabaseName("UX_CreditTx_Idempotency");
        b.HasIndex(x => new { x.Type, x.RefId }).IsUnique()
            .HasFilter("[RefId] IS NOT NULL").HasDatabaseName("UX_CreditTx_TypeRef");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_CreditTx_User").OnDelete(DeleteBehavior.NoAction);
    }
}

public class ListingPromotionConfiguration : IEntityTypeConfiguration<ListingPromotion>
{
    public void Configure(EntityTypeBuilder<ListingPromotion> b)
    {
        b.ToTable("ListingPromotions", t =>
        {
            t.HasCheckConstraint("CK_ListPromo_Type", "[PromotionType] IN ('Featured','TopOfList','Highlight')");
            t.HasCheckConstraint("CK_ListPromo_Status", "[Status] IN ('Active','Expired','Cancelled')");
            t.HasCheckConstraint("CK_ListPromo_Time", "[EndsAtUtc] > [StartsAtUtc]");
            t.HasCheckConstraint("CK_ListPromo_Cost", "[CreditCost] >= 0");
        });
        b.HasKey(x => x.ListingPromotionId);
        b.Property(x => x.ListingPromotionId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.PromotionType).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.CreditCost).HasColumnType("decimal(12,2)");
        b.Property(x => x.StartsAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.EndsAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.ListingPromotionStatus.Active);
        // Y-08: NOT NULL — promotion is created only after the credit debit succeeds (pairs with B-02).
        b.Property(x => x.CreditTransactionId).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.ProductId).HasDatabaseName("IX_ListPromo_Product");
        b.HasIndex(x => x.EndsAtUtc).HasFilter("[Status] = 'Active'").HasDatabaseName("IX_ListPromo_Active_End");
        // Y-08: each PromoSpend debit backs exactly one promotion (no missing/duplicated debit).
        b.HasIndex(x => x.CreditTransactionId).IsUnique().HasDatabaseName("UQ_ListPromo_CreditTx");
        b.HasOne(x => x.Product).WithMany(x => x.Promotions)
            .HasForeignKey(x => x.ProductId).HasConstraintName("FK_ListPromo_Product").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_ListPromo_User").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.PromotionPackage).WithMany(x => x.ListingPromotions)
            .HasForeignKey(x => x.PromotionPackageId).HasConstraintName("FK_ListPromo_Package").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.CreditTransaction).WithMany()
            .HasForeignKey(x => x.CreditTransactionId).IsRequired()
            .HasConstraintName("FK_ListPromo_CreditTx").OnDelete(DeleteBehavior.NoAction);
    }
}
