using System.ComponentModel.DataAnnotations;

namespace Marketplace.Web.Models.Account;

/// <summary>FR-01: registration. Requires explicit PDPA consent; optional referral code (FR-28, single-level).
/// On success the user starts a 3-month free trial (FR-27) — price + trial-end disclosed up front.</summary>
public class RegisterViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, Phone]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "รหัสผ่านไม่ตรงกัน")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>FR-28: optional referrer code (e.g. "NEON-1234"). Single-level only.</summary>
    public string? ReferralCode { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "ต้องยอมรับนโยบาย PDPA และเงื่อนไขการใช้งาน")]
    public bool ConsentPdpa { get; set; }
}

/// <summary>FR-01: login (email + password). Issues the auth cookie on success.</summary>
public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
