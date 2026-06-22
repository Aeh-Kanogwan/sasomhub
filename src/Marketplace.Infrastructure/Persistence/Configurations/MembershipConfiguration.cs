using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("Memberships", t =>
        {
            t.HasCheckConstraint("CK_Membership_Status", "[Status] IN ('Trial','Active','Expired','Cancelled')");
            t.HasCheckConstraint("CK_Membership_Trial",
                "[TrialEndsAtUtc] IS NULL OR [TrialStartsAtUtc] IS NULL OR [TrialEndsAtUtc] > [TrialStartsAtUtc]");
            // Y-05: a Trial membership MUST have an expiry instant (no never-ending free trial; FR-27 / Legal #8).
            t.HasCheckConstraint("CK_Membership_TrialHasEnd",
                "[Status] <> 'Trial' OR [TrialEndsAtUtc] IS NOT NULL");
            t.HasCheckConstraint("CK_Membership_PaidAmount",
                "[PaidAmountTHB] IS NULL OR [PaidAmountTHB] >= 0");
        });
        b.HasKey(x => x.MembershipId);
        b.Property(x => x.MembershipId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(Domain.Enums.MembershipStatus.Trial);
        b.Property(x => x.StartAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.EndAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.TrialStartsAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.TrialEndsAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.PaidThroughUtc).HasColumnType("datetime2(3)");
        // Y-06/B-04: price snapshot of the paid cycle — no retroactive pricing (FR-31).
        b.Property(x => x.PaidAmountTHB).HasColumnType("decimal(10,2)");
        // Flow B: target tier of a requested-but-not-yet-paid upgrade (NULL once applied/cleared).
        b.Property(x => x.PendingUpgradeTierId).HasColumnType("tinyint");
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.RowVersion).IsRowVersion();
        // One live membership per user (Trial or Active) — FR-27
        b.HasIndex(x => x.UserId).IsUnique()
            .HasFilter("[Status] IN ('Trial','Active')").HasDatabaseName("UX_Membership_LivePerUser");
        // S-03: fast scan for the expiry flip + 14/3/1-day notification sweep (FR-27).
        b.HasIndex(x => new { x.Status, x.PaidThroughUtc, x.TrialEndsAtUtc }).HasDatabaseName("IX_Membership_Expiry");
        b.HasOne(x => x.User).WithMany(x => x.Memberships)
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Membership_Users").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MembershipTier).WithMany(x => x.Memberships)
            .HasForeignKey(x => x.MembershipTierId).HasConstraintName("FK_Membership_Tier").OnDelete(DeleteBehavior.Restrict);
    }
}
