using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Marketplace.Application.Common;
using Microsoft.IdentityModel.Tokens;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// Strongly-typed JWT settings. The signing key is supplied by the host (Program.cs) AFTER its
/// fail-fast check so there is one place that decides "is this key safe to run with" — this service
/// never reaches into configuration for the secret itself.
/// </summary>
public sealed class JwtOptions
{
    public string SigningKey { get; init; } = null!;
    public string Issuer { get; init; } = "marketplace.local";
    public string Audience { get; init; } = "marketplace.local";
    public int AccessTokenMinutes { get; init; } = 30;   // NFR-S1: short-lived access token
    public int RefreshTokenDays { get; init; } = 14;     // long-lived refresh token
}

/// <summary>
/// HS256 token service (NFR-S1/S3). Issues a short-lived access token carrying identity + RBAC role +
/// membership snapshot claims (FR-06/FR-10 gate), plus a long-lived refresh token (a separate JWT marked
/// <c>token_use=refresh</c>). Validates refresh tokens on /api/auth/refresh.
///
/// Custom claim names mirror what Program.cs's AuthorizationPolicies read:
///   role -> ClaimTypes.Role ("Admin" policy), MembershipActive -> "MembershipActive" policy.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    /// <summary>Marks a token as the refresh token (vs. an access token) so one cannot be used as the other.</summary>
    public const string TokenUseClaim = "token_use";
    public const string AccessTokenUse = "access";
    public const string RefreshTokenUse = "refresh";

    // Membership snapshot claims (FR-06/FR-10). MembershipActive drives the "MembershipActive" policy.
    public const string MembershipTierClaim = "MembershipTier";
    public const string MembershipStatusClaim = "MembershipStatus";
    public const string MembershipActiveClaim = "MembershipActive";

    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenService(JwtOptions options)
    {
        _options = options;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public IssuedTokens Issue(TokenClaims claims)
    {
        var now = DateTime.UtcNow;
        var accessExpires = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = now.AddDays(_options.RefreshTokenDays);

        var access = BuildAccessToken(claims, now, accessExpires);
        var refresh = BuildRefreshToken(claims.UserId, now, refreshExpires);

        return new IssuedTokens(access, accessExpires, refresh, refreshExpires);
    }

    public Guid? ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        try
        {
            var principal = _handler.ValidateToken(refreshToken, RefreshValidationParameters(), out _);

            // Must explicitly be a refresh token — never accept an access token here.
            if (principal.FindFirst(TokenUseClaim)?.Value != RefreshTokenUse)
                return null;

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var userId) ? userId : null;
        }
        catch (SecurityTokenException)
        {
            return null; // expired / wrong signature / wrong issuer-audience -> not valid
        }
        catch (ArgumentException)
        {
            return null; // malformed token string
        }
    }

    private string BuildAccessToken(TokenClaims c, DateTime now, DateTime expires)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, c.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, c.Email),
            new(ClaimTypes.NameIdentifier, c.UserId.ToString()),
            new(ClaimTypes.Role, c.Role.ToString()),
            new(TokenUseClaim, AccessTokenUse),
            new(MembershipActiveClaim, c.MembershipActive ? "true" : "false"),
        };
        if (c.MembershipTier is not null)
            claims.Add(new Claim(MembershipTierClaim, c.MembershipTier));
        if (c.MembershipStatus is not null)
            claims.Add(new Claim(MembershipStatusClaim, c.MembershipStatus.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _credentials);
        return _handler.WriteToken(token);
    }

    private string BuildRefreshToken(Guid userId, DateTime now, DateTime expires)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(TokenUseClaim, RefreshTokenUse),
        };
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _credentials);
        return _handler.WriteToken(token);
    }

    private TokenValidationParameters RefreshValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = _options.Issuer,
        ValidAudience = _options.Audience,
        IssuerSigningKey = _credentials.Key,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}
