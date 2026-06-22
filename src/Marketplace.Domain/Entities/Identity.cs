using Marketplace.Domain.Enums;

namespace Marketplace.Domain.Entities;

// ============================ 2. USERS & PROFILE ==========================

/// <summary>dbo.Users — root identity. PDPA soft-delete/anonymize (IsDeleted/IsAnonymized).</summary>
public class User
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = null!;
    public string NormalizedEmail { get; set; } = null!;
    public string? PasswordHash { get; set; }     // null => NDID/social
    public string? PhoneNumber { get; set; }
    public UserRole Role { get; set; } = UserRole.Member;
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Active;
    public bool EmailConfirmed { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsAnonymized { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = null!;

    // Navigation
    public UserProfile? Profile { get; set; }
    public TrustScore? TrustScore { get; set; }
    public CreditAccount? CreditAccount { get; set; }
    public ReferralCode? ReferralCode { get; set; }
    public ICollection<KycVerification> KycVerifications { get; set; } = new List<KycVerification>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
}

/// <summary>dbo.UserProfiles — 1:1 with Users, ON DELETE CASCADE. Display data kept apart from credentials.</summary>
public class UserProfile
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? ProvinceCode { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public User User { get; set; } = null!;
}

// ============================ 3. KYC (e-KYC / NDID) =======================

/// <summary>dbo.KycVerifications — stores status + provider ref only, never raw ID images (Legal #4, FR-02).</summary>
public class KycVerification
{
    public Guid KycVerificationId { get; set; }
    public Guid UserId { get; set; }
    public byte KycStatusId { get; set; }
    public string Provider { get; set; } = null!;
    public string? ProviderReference { get; set; }
    public byte VerificationLevel { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public KycStatus KycStatus { get; set; } = null!;
    public KycSensitiveData? SensitiveData { get; set; }
}

/// <summary>dbo.KycSensitiveData — 1:1 optional, [ENCRYPTED] columns + retention (FR-03).</summary>
public class KycSensitiveData
{
    public Guid KycVerificationId { get; set; }
    public string? FullNameMasked { get; set; }       // [ENCRYPTED]
    public byte[]? NationalIdHash { get; set; }        // [ENCRYPTED] hash, not raw number
    public DateTime RetentionExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public KycVerification KycVerification { get; set; } = null!;
}
