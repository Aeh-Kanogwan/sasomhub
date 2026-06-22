using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", t =>
        {
            t.HasCheckConstraint("CK_Users_Role", "[Role] IN ('Member','Admin','Support')");
            // B-05/G-5: PendingBanReview = auto-flagged, awaiting human Admin confirmation (no permanent auto-ban).
            t.HasCheckConstraint("CK_Users_Status", "[AccountStatus] IN ('Active','Suspended','PendingBanReview','Banned')");
        });
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.PhoneNumber).HasMaxLength(32);
        b.Property(x => x.Role).HasConversion<string>().HasColumnType("varchar(20)").HasDefaultValue(Domain.Enums.UserRole.Member);
        b.Property(x => x.AccountStatus).HasConversion<string>().HasColumnType("varchar(20)").HasDefaultValue(Domain.Enums.AccountStatus.Active);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property(x => x.DeletedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.RowVersion).IsRowVersion();
        // Filtered unique email (supports anonymize/re-register) — UX_Users_NormalizedEmail WHERE IsDeleted=0
        b.HasIndex(x => x.NormalizedEmail).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_Users_NormalizedEmail");

        b.HasOne(x => x.Profile).WithOne(x => x.User).HasForeignKey<UserProfile>(x => x.UserId);
        b.HasOne(x => x.TrustScore).WithOne(x => x.User).HasForeignKey<TrustScore>(x => x.UserId);
        b.HasOne(x => x.CreditAccount).WithOne(x => x.User).HasForeignKey<CreditAccount>(x => x.UserId);
        b.HasOne(x => x.ReferralCode).WithOne(x => x.User).HasForeignKey<ReferralCode>(x => x.UserId);
    }
}

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("UserProfiles");
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
        b.Property(x => x.AvatarUrl).HasMaxLength(512);
        b.Property(x => x.Bio).HasMaxLength(1000);
        b.Property(x => x.ProvinceCode).HasColumnType("varchar(10)");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.User).WithOne(x => x.Profile)
            .HasForeignKey<UserProfile>(x => x.UserId)
            .HasConstraintName("FK_UserProfiles_Users")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class KycVerificationConfiguration : IEntityTypeConfiguration<KycVerification>
{
    public void Configure(EntityTypeBuilder<KycVerification> b)
    {
        b.ToTable("KycVerifications");
        b.HasKey(x => x.KycVerificationId);
        b.Property(x => x.KycVerificationId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Provider).HasColumnType("varchar(40)").IsRequired();
        b.Property(x => x.ProviderReference).HasColumnType("varchar(128)");
        b.Property(x => x.VerificationLevel).HasDefaultValue((byte)0);
        b.Property(x => x.VerifiedAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_Kyc_UserId");
        b.HasOne(x => x.User).WithMany(x => x.KycVerifications)
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Kyc_Users").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.KycStatus).WithMany(x => x.KycVerifications)
            .HasForeignKey(x => x.KycStatusId).HasConstraintName("FK_Kyc_Status").OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SensitiveData).WithOne(x => x.KycVerification)
            .HasForeignKey<KycSensitiveData>(x => x.KycVerificationId);
    }
}

public class KycSensitiveDataConfiguration : IEntityTypeConfiguration<KycSensitiveData>
{
    public void Configure(EntityTypeBuilder<KycSensitiveData> b)
    {
        b.ToTable("KycSensitiveData");
        b.HasKey(x => x.KycVerificationId);
        b.Property(x => x.KycVerificationId).ValueGeneratedNever();
        b.Property(x => x.FullNameMasked).HasMaxLength(256);          // [ENCRYPTED] at deploy
        b.Property(x => x.NationalIdHash).HasColumnType("varbinary(64)"); // [ENCRYPTED] hash
        b.Property(x => x.RetentionExpiresAtUtc).HasColumnType("datetime2(3)").IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.KycVerification).WithOne(x => x.SensitiveData)
            .HasForeignKey<KycSensitiveData>(x => x.KycVerificationId)
            .HasConstraintName("FK_KycSens_Kyc").OnDelete(DeleteBehavior.Cascade);
    }
}
