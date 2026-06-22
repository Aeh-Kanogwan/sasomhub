using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

public class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> b)
    {
        // B-06/G-6: APPEND-ONLY PDPA consent log (FR-01/04). DB trigger TR_ConsentRecords_NoModify
        // rejects UPDATE/DELETE at the database layer (THROW 51002) — EF must NEVER UPDATE/DELETE
        // these rows; record a NEW consent event instead. IP stored as hash only.
        b.ToTable("ConsentRecords");
        b.HasKey(x => x.ConsentRecordId);
        b.Property(x => x.ConsentRecordId).UseIdentityColumn();
        b.Property(x => x.ConsentType).HasColumnType("varchar(40)").IsRequired();
        b.Property(x => x.DocumentVersion).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.SourceIpHash).HasColumnType("varbinary(32)");
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.UserId, x.ConsentType, x.CreatedAtUtc }).HasDatabaseName("IX_Consent_User_Type");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Consent_User").OnDelete(DeleteBehavior.NoAction);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        // B-06/G-6: APPEND-ONLY, immutable audit trail (FR-24, NFR-A1). DB trigger TR_AuditLogs_NoModify
        // rejects UPDATE/DELETE at the database layer (THROW 51001) — EF must NEVER UPDATE/DELETE these rows.
        b.ToTable("AuditLogs");
        b.HasKey(x => x.AuditLogId);
        b.Property(x => x.AuditLogId).UseIdentityColumn();
        b.Property(x => x.Action).HasColumnType("varchar(60)").IsRequired();
        b.Property(x => x.EntityType).HasColumnType("varchar(60)").IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.IpAddressHash).HasColumnType("varbinary(32)");
        // S-06: per-transaction correlation id (NFR-A2) — ties audit rows in one logical op for AML tracing.
        b.Property(x => x.CorrelationId);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAtUtc }).HasDatabaseName("IX_Audit_Entity");
        b.HasIndex(x => new { x.ActorUserId, x.CreatedAtUtc }).HasDatabaseName("IX_Audit_Actor");
        b.HasIndex(x => x.CorrelationId).HasFilter("[CorrelationId] IS NOT NULL").HasDatabaseName("IX_Audit_Correlation");
        b.HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => x.ActorUserId).HasConstraintName("FK_Audit_Actor").OnDelete(DeleteBehavior.NoAction);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        // B-01/G-1: advance-notice log (FR-27) + idempotency guard for the notification worker.
        b.ToTable("Notifications", t =>
        {
            t.HasCheckConstraint("CK_Notif_Type",
                "[Type] IN ('TrialExpiring','RenewalDue','RenewalCharged','MembershipExpired','PromotionExpiring','PenaltyIssued','AppealUpdate')");
            t.HasCheckConstraint("CK_Notif_Channel", "[Channel] IN ('Email','InApp','Sms')");
            t.HasCheckConstraint("CK_Notif_Status", "[Status] IN ('Pending','Sent','Failed','Skipped')");
            t.HasCheckConstraint("CK_Notif_Milestone", "[Milestone] IS NULL OR [Milestone] IN (14,3,1)");
        });
        b.HasKey(x => x.NotificationId);
        b.Property(x => x.NotificationId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.Type).HasConversion<string>().HasColumnType("varchar(30)").IsRequired();
        b.Property(x => x.Channel).HasConversion<string>().HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.ScheduledForUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.SentAtUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.Status).HasConversion<string>().HasColumnType("varchar(20)")
            .HasDefaultValue(NotificationStatus.Pending);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc }).HasDatabaseName("IX_Notif_User");
        // worker picks due, not-yet-sent notices
        b.HasIndex(x => x.ScheduledForUtc).HasFilter("[Status] = 'Pending'").HasDatabaseName("IX_Notif_Due");
        // B-01/G-1: dedupe — at most one lifecycle notice per (user, type, membership, milestone).
        b.HasIndex(x => new { x.UserId, x.Type, x.RelatedMembershipId, x.Milestone }).IsUnique()
            .HasFilter("[RelatedMembershipId] IS NOT NULL AND [Milestone] IS NOT NULL")
            .HasDatabaseName("UX_Notif_NoDup");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).HasConstraintName("FK_Notif_User").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.RelatedMembership).WithMany()
            .HasForeignKey(x => x.RelatedMembershipId).HasConstraintName("FK_Notif_Membership").OnDelete(DeleteBehavior.NoAction);
    }
}

public class AppraisalOpinionConfiguration : IEntityTypeConfiguration<AppraisalOpinion>
{
    public void Configure(EntityTypeBuilder<AppraisalOpinion> b)
    {
        // B-03/G-3: opinion only — platform does NOT warrant authenticity (LEGAL #2, FR-07).
        // DisclaimerVersion proves which disclaimer text was shown at the time.
        b.ToTable("AppraisalOpinions");
        b.HasKey(x => x.AppraisalOpinionId);
        b.Property(x => x.AppraisalOpinionId).HasDefaultValueSql("NEWSEQUENTIALID()");
        b.Property(x => x.AppraiserName).HasMaxLength(120).IsRequired();
        b.Property(x => x.OpinionText).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.DisclaimerVersion).HasColumnType("varchar(20)").IsRequired();
        b.Property(x => x.IsPublished).HasDefaultValue(false);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasIndex(x => x.ProductId).HasFilter("[IsPublished] = 1").HasDatabaseName("IX_Appraisal_Product");
        b.HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).HasConstraintName("FK_Appraisal_Product").OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.AppraiserUser).WithMany()
            .HasForeignKey(x => x.AppraiserUserId).HasConstraintName("FK_Appraisal_User").OnDelete(DeleteBehavior.NoAction);
    }
}

public class ConfigVersionConfiguration : IEntityTypeConfiguration<ConfigVersion>
{
    public void Configure(EntityTypeBuilder<ConfigVersion> b)
    {
        // B-04/G-4: versioned, immutable config history (FR-31). Never applied retroactively.
        b.ToTable("ConfigVersions");
        b.HasKey(x => x.ConfigVersionId);
        b.Property(x => x.ConfigVersionId).UseIdentityColumn();
        b.Property(x => x.ConfigKey).HasColumnType("varchar(80)").IsRequired();
        b.Property(x => x.Value).HasMaxLength(400).IsRequired();
        b.Property(x => x.EffectiveFromUtc).HasColumnType("datetime2(3)");
        b.Property(x => x.Note).HasMaxLength(400);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        // one config version per key per effective instant (no ambiguous overlapping versions).
        // This unique index also serves the resolve query "value of key K at time T" =
        // latest EffectiveFromUtc <= T (schema's separate IX_ConfigVer_Key_Effective DESC is redundant here).
        b.HasIndex(x => new { x.ConfigKey, x.EffectiveFromUtc }).IsUnique().HasDatabaseName("UQ_ConfigVer_KeyEffective");
        b.HasOne(x => x.CreatedByUser).WithMany()
            .HasForeignKey(x => x.CreatedByUserId).HasConstraintName("FK_ConfigVer_CreatedBy").OnDelete(DeleteBehavior.NoAction);
    }
}
