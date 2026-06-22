using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Marketplace.Application.Common;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Services;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

public class JwtTokenServiceTests
{
    private static JwtTokenService Build() => new(new JwtOptions
    {
        SigningKey = "unit-test-signing-key-which-is-definitely-long-enough-32+",
        Issuer = "test.issuer",
        Audience = "test.audience",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 14,
    });

    private static TokenClaims SampleClaims(Guid userId, UserRole role = UserRole.Member, bool active = true)
        => new(userId, "user@test.local", role, "Premium", MembershipStatus.Active, active);

    [Fact]
    public void Issue_access_token_carries_identity_role_and_membership_claims()
    {
        var svc = Build();
        var userId = Guid.NewGuid();

        var tokens = svc.Issue(SampleClaims(userId, UserRole.Admin, active: true));

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.True(tokens.AccessTokenExpiresAtUtc < tokens.RefreshTokenExpiresAtUtc);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal("test.issuer", jwt.Issuer);
        Assert.Equal(userId.ToString(), jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("Admin", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("true", jwt.Claims.First(c => c.Type == JwtTokenService.MembershipActiveClaim).Value);
        Assert.Equal("Premium", jwt.Claims.First(c => c.Type == JwtTokenService.MembershipTierClaim).Value);
        Assert.Equal(JwtTokenService.AccessTokenUse,
            jwt.Claims.First(c => c.Type == JwtTokenService.TokenUseClaim).Value);
    }

    [Fact]
    public void MembershipActive_false_is_stamped_when_inactive()
    {
        var svc = Build();
        var tokens = svc.Issue(SampleClaims(Guid.NewGuid(), active: false));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal("false", jwt.Claims.First(c => c.Type == JwtTokenService.MembershipActiveClaim).Value);
    }

    [Fact]
    public void ValidateRefreshToken_returns_user_id_for_a_genuine_refresh_token()
    {
        var svc = Build();
        var userId = Guid.NewGuid();
        var tokens = svc.Issue(SampleClaims(userId));

        var resolved = svc.ValidateRefreshToken(tokens.RefreshToken);

        Assert.Equal(userId, resolved);
    }

    [Fact]
    public void ValidateRefreshToken_rejects_an_access_token()
    {
        // An access token must NOT be accepted at the refresh endpoint (token_use guard).
        var svc = Build();
        var tokens = svc.Issue(SampleClaims(Guid.NewGuid()));

        Assert.Null(svc.ValidateRefreshToken(tokens.AccessToken));
    }

    [Fact]
    public void ValidateRefreshToken_rejects_garbage_and_empty()
    {
        var svc = Build();
        Assert.Null(svc.ValidateRefreshToken(""));
        Assert.Null(svc.ValidateRefreshToken("not-a-jwt"));
    }

    [Fact]
    public void ValidateRefreshToken_rejects_a_token_signed_with_a_different_key()
    {
        var issuer = Build();
        var tokens = issuer.Issue(SampleClaims(Guid.NewGuid()));

        var attacker = new JwtTokenService(new JwtOptions
        {
            SigningKey = "a-totally-different-signing-key-also-long-enough-32+",
            Issuer = "test.issuer",
            Audience = "test.audience",
        });

        Assert.Null(attacker.ValidateRefreshToken(tokens.RefreshToken));
    }
}
