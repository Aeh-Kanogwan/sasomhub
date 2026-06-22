namespace Marketplace.Application.Auth;

// FR-01/FR-04 + NFR-S1: request/response shapes for the Auth endpoints. Kept in Application so the
// Api controller and any future client SDK share one contract. Validation lives at the controller
// edge (DataAnnotations) + the service layer; these are plain transport records.

/// <summary>
/// FR-01: register a Normal member. PDPA consent is mandatory (<see cref="AcceptPdpaConsent"/> must be
/// true). An optional referral code links the new user to a referrer (FR-28, single-level).
/// </summary>
public sealed record RegisterRequest(
    string Email,
    string Password,
    string? PhoneNumber,
    string? DisplayName,
    bool AcceptPdpaConsent,
    string? ConsentDocumentVersion,
    string? ReferralCode);

/// <summary>Result of a successful registration (the caller then logs in to obtain tokens).</summary>
public sealed record RegisterResultDto(Guid UserId, string Email, Guid MembershipId, DateTime TrialEndsAtUtc);

/// <summary>NFR-S1: credentials exchanged for a JWT pair.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>The token pair returned by login/refresh.</summary>
public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    DateTime AccessTokenExpiresAtUtc,
    DateTime RefreshTokenExpiresAtUtc);

/// <summary>NFR-S1: exchange a valid refresh token for a fresh access token (+ rotated refresh token).</summary>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>FR-04: record (grant/withdraw) a PDPA consent for the authenticated caller.</summary>
public sealed record ConsentRequest(string ConsentType, string DocumentVersion, bool IsGranted);
