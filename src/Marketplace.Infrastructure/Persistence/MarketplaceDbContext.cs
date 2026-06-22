using Marketplace.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Marketplace.Infrastructure.Persistence;

/// <summary>
/// EF Core (Code-First) context mirroring sql/schema.sql (33 tables).
/// All IEntityTypeConfiguration are applied from this assembly; lookup seed lives in SeedData.
/// </summary>
public class MarketplaceDbContext : DbContext
{
    public MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options) : base(options) { }

    // Lookups
    public DbSet<KycStatus> KycStatuses => Set<KycStatus>();
    public DbSet<BlacklistReasonCode> BlacklistReasonCodes => Set<BlacklistReasonCode>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MembershipTier> MembershipTiers => Set<MembershipTier>();
    public DbSet<PromotionPackage> PromotionPackages => Set<PromotionPackage>();

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<KycVerification> KycVerifications => Set<KycVerification>();
    public DbSet<KycSensitiveData> KycSensitiveData => Set<KycSensitiveData>();

    // Membership
    public DbSet<Membership> Memberships => Set<Membership>();

    // Catalog & Auction
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<AppraisalOpinion> AppraisalOpinions => Set<AppraisalOpinion>();  // B-03/G-3

    // Trade
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionStatusHistory> TransactionStatusHistory => Set<TransactionStatusHistory>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Dispute> Disputes => Set<Dispute>();

    // Reputation
    public DbSet<TrustScore> TrustScores => Set<TrustScore>();
    public DbSet<TrustScoreHistory> TrustScoreHistory => Set<TrustScoreHistory>();
    public DbSet<PenaltyAction> PenaltyActions => Set<PenaltyAction>();
    public DbSet<BlacklistEntry> BlacklistEntries => Set<BlacklistEntry>();

    // Revenue & Growth
    public DbSet<FeeInvoice> FeeInvoices => Set<FeeInvoice>();
    public DbSet<PaymentSlip> PaymentSlips => Set<PaymentSlip>();        // Flow B membership payment
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<CreditAccount> CreditAccounts => Set<CreditAccount>();
    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();
    public DbSet<ListingPromotion> ListingPromotions => Set<ListingPromotion>();

    // Compliance
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();          // B-01/G-1
    public DbSet<ConfigVersion> ConfigVersions => Set<ConfigVersion>();       // B-04/G-4

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        SeedData.Apply(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }
}
