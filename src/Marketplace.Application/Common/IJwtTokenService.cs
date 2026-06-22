using Marketplace.Domain.Enums;

namespace Marketplace.Application.Common;

/// <summary>
/// The set of claims an access token carries about the caller. Membership claims (FR-06/FR-10 gate)
/// are stamped at issue time so the API can authorize listing/bid without a DB round-trip on every
/// request; they are necessarily a point-in-time snapshot (see <see cref="IJwtTokenService"/> remarks).
/// </summary>
public sealed record TokenClaims(
    Guid UserId,
    string Email,
    UserRole Role,
    string? MembershipTier,
    MembershipStatus? MembershipStatus,
    bool MembershipActive);

/// <summary>An issued access token + its long-lived refresh token (NFR-S1: JWT + refresh).</summary>
public sealed record IssuedTokens(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string TokenType = "Bearer");

/// <summary>
/// NFR-S1/S3: issues short-lived HS256 access tokens + a long-lived refresh token, and validates a
/// refresh token presented at /api/auth/refresh. The signing key comes from a secret store (never repo).
///
/// Refresh-token strategy (MVP): the refresh token is itself a self-contained HS256 JWT marked
/// <c>token_use=refresh</c>, signed with the same key but a longer lifetime. This needs no extra DB
/// table (the schema has none for refresh tokens). Trade-off: tokens cannot be individually revoked
/// before they expire — acceptable for the MVP because access tokens are short-lived and the refresh
/// lifetime is bounded; server-side revocation (a hashed-token table) is a documented follow-up.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Issue an access token (short-lived) + refresh token (long-lived) for the given claims.</summary>
    IssuedTokens Issue(TokenClaims claims);

    /// <summary>
    /// Validate a refresh token and return the user id it was issued for, or null if it is invalid /
    /// expired / not a refresh token. The caller re-loads the user + membership to mint a fresh access token
    /// (so a stale membership snapshot in the refresh token is never trusted).
    /// </summary>
    Guid? ValidateRefreshToken(string refreshToken);
}
