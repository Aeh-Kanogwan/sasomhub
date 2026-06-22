using System.ComponentModel.DataAnnotations;
using Marketplace.Application.Auth;
using Marketplace.Application.Common;
using Marketplace.Application.Memberships;
using Marketplace.Application.Referrals;
using Marketplace.Application.Reputation;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Controllers;

/// <summary>
/// Account / KYC endpoints (FR-01..FR-04) + JWT issuance (NFR-S1).
/// FR-01: signup requires PDPA consent; creates User (Normal), TrustScore=100, TRIAL membership (FR-27),
/// an optional single-level referral link (FR-28). Login issues a JWT pair; Refresh rotates it.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    // Normal tier = the seeded MembershipTierId 1 (FR-27 free trial starts on Normal).
    private const byte NormalTierId = 1;

    private readonly MarketplaceDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMembershipService _membership;
    private readonly IReferralService _referral;
    private readonly IReferralCodeGenerator _referralCodeGenerator;
    private readonly ITrustScoreService _trustScore;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        MarketplaceDbContext db,
        IPasswordHasher passwordHasher,
        IMembershipService membership,
        IReferralService referral,
        IReferralCodeGenerator referralCodeGenerator,
        ITrustScoreService trustScore,
        IJwtTokenService jwt,
        ILogger<AuthController> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _membership = membership;
        _referral = referral;
        _referralCodeGenerator = referralCodeGenerator;
        _trustScore = trustScore;
        _jwt = jwt;
        _logger = logger;
    }

    /// <summary>
    /// FR-01: register a Normal member (PDPA consent mandatory). Creates the user + profile + consent record,
    /// initialises Trust Score = 100 (FR-13), starts the 3-month free trial (FR-27), generates the user's
    /// single referral code, and — if a referral code was supplied — links the referrer (FR-28, single-level).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!new EmailAddressAttribute().IsValid(request.Email))
            return Problem("A valid email is required.");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return Problem("Password must be at least 8 characters.");
        // FR-01: PDPA consent is a hard precondition — no account without it.
        if (!request.AcceptPdpaConsent)
            return Problem("You must accept the PDPA consent to register.");

        var normalized = request.Email.Trim().ToUpperInvariant();
        var emailExists = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.NormalizedEmail == normalized && !u.IsDeleted, ct);
        if (emailExists)
            return Problem("An account with this email already exists.");

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = _passwordHasher.Hash(request.Password),  // NFR-S1: store hash only
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Role = UserRole.Member,
            AccountStatus = AccountStatus.Active,
            EmailConfirmed = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Profile = new UserProfile
            {
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? request.Email.Split('@')[0]
                    : request.DisplayName.Trim(),
                UpdatedAtUtc = now,
            },
        };

        // FR-01/FR-04 + NFR-P1: append-only PDPA consent record (versioned + timestamped).
        _db.ConsentRecords.Add(new ConsentRecord
        {
            User = user,
            ConsentType = "PDPA",
            DocumentVersion = string.IsNullOrWhiteSpace(request.ConsentDocumentVersion)
                ? "v1" : request.ConsentDocumentVersion.Trim(),
            IsGranted = true,
            CreatedAtUtc = now,
        });

        // FR-28: the user's own single-level referral code (one per user).
        _db.ReferralCodes.Add(new ReferralCode
        {
            User = user,
            Code = await _referralCodeGenerator.GenerateUniqueAsync(ct),
            IsActive = true,
            CreatedAtUtc = now,
        });

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) // lost race on UX_Users_NormalizedEmail / UQ_RefCode_Code
        {
            return Problem("An account with this email already exists.");
        }

        // FR-13: Trust Score = 100 + initial history row (append-only).
        var trust = await _trustScore.EnsureInitializedAsync(user.UserId, ct);
        if (!trust.Succeeded)
            _logger.LogError("Trust score init failed for new user {UserId}: {Error}", user.UserId, trust.Error);

        // FR-27: start the 3-month trial (separate save inside the service; one live membership per user).
        var trial = await _membership.StartTrialAsync(new StartTrialRequest(user.UserId, NormalTierId), ct);
        if (!trial.Succeeded)
        {
            _logger.LogError("Trial start failed for new user {UserId}: {Error}", user.UserId, trial.Error);
            return Problem(trial.Error);
        }

        // FR-28: optional referral link. A bad code must not fail registration — log and continue
        // (the reward only pays out later on the qualifying trigger anyway).
        if (!string.IsNullOrWhiteSpace(request.ReferralCode))
        {
            var link = await _referral.RegisterAsync(
                new RegisterReferralRequest(user.UserId, request.ReferralCode.Trim()), ct);
            if (!link.Succeeded)
                _logger.LogInformation("Referral link skipped for {UserId}: {Error}", user.UserId, link.Error);
        }

        var dto = new RegisterResultDto(user.UserId, user.Email, trial.Value!.MembershipId, trial.Value!.TrialEndsAtUtc!.Value);
        return CreatedAtAction(nameof(Register), new { id = user.UserId }, dto);
    }

    /// <summary>NFR-S1: issue a JWT access token (+ refresh) on valid credentials. Fails closed on any mismatch.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var normalized = (request.Email ?? string.Empty).Trim().ToUpperInvariant();
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && !u.IsDeleted, ct);

        // Same generic message whether the email is unknown or the password is wrong (no user enumeration).
        if (user is null || user.PasswordHash is null
            || !_passwordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            return Problem(detail: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

        if (user.AccountStatus is AccountStatus.Banned or AccountStatus.Suspended)
            return Problem(detail: "Account is not permitted to sign in.", statusCode: StatusCodes.Status403Forbidden);

        var tokens = await IssueForUserAsync(user.UserId, ct);
        return Ok(tokens);
    }

    /// <summary>NFR-S1: exchange a valid refresh token for a fresh access token (membership snapshot re-read).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var userId = _jwt.ValidateRefreshToken(request.RefreshToken ?? string.Empty);
        if (userId is null)
            return Problem(detail: "Invalid or expired refresh token.", statusCode: StatusCodes.Status401Unauthorized);

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId.Value && !u.IsDeleted, ct);
        if (user is null || user.AccountStatus is AccountStatus.Banned or AccountStatus.Suspended)
            return Problem(detail: "Account is no longer permitted.", statusCode: StatusCodes.Status401Unauthorized);

        var tokens = await IssueForUserAsync(user.UserId, ct);
        return Ok(tokens);
    }

    /// <summary>
    /// FR-02: receive an NDID e-KYC result. NOT IMPLEMENTED in this phase — KYC provider integration
    /// (NDID callback verification, KycVerification/KycSensitiveData persistence + Always Encrypted) is a
    /// later phase. Returning 501 keeps the contract explicit rather than silently accepting unverified data.
    /// </summary>
    [HttpPost("kyc/ndid-callback")]
    [AllowAnonymous]
    public IActionResult NdidCallback()
        => StatusCode(StatusCodes.Status501NotImplemented,
            "NDID e-KYC integration (FR-02) is a later phase; no KYC provider is wired yet.");

    /// <summary>FR-04: record a PDPA consent (grant/withdraw) for the authenticated caller (append-only).</summary>
    [HttpPost("consent")]
    [Authorize]
    public async Task<IActionResult> RecordConsent([FromBody] ConsentRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Problem(detail: "Not authenticated.", statusCode: StatusCodes.Status401Unauthorized);
        if (string.IsNullOrWhiteSpace(request.ConsentType) || string.IsNullOrWhiteSpace(request.DocumentVersion))
            return Problem("ConsentType and DocumentVersion are required.");

        _db.ConsentRecords.Add(new ConsentRecord
        {
            UserId = userId,
            ConsentType = request.ConsentType.Trim(),
            DocumentVersion = request.DocumentVersion.Trim(),
            IsGranted = request.IsGranted,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Build the token claims from the user + live membership snapshot and issue the pair.</summary>
    private async Task<TokenResponse> IssueForUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.UserId == userId, ct);

        // Membership snapshot for FR-06/FR-10 claims. Picks the single live (Trial/Active) row if any.
        var membership = await _db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId &&
                        (m.Status == MembershipStatus.Trial || m.Status == MembershipStatus.Active))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new { m.Status, m.MembershipTierId })
            .FirstOrDefaultAsync(ct);

        var active = await _membership.IsMembershipActiveAsync(userId, ct);
        string? tierCode = null;
        if (membership is not null)
            tierCode = await _db.MembershipTiers.AsNoTracking()
                .Where(t => t.MembershipTierId == membership.MembershipTierId)
                .Select(t => t.Code).FirstOrDefaultAsync(ct);

        var claims = new TokenClaims(
            UserId: user.UserId,
            Email: user.Email,
            Role: user.Role,
            MembershipTier: tierCode,
            MembershipStatus: membership?.Status,
            MembershipActive: active);

        var issued = _jwt.Issue(claims);
        return new TokenResponse(
            issued.AccessToken, issued.RefreshToken, issued.TokenType,
            issued.AccessTokenExpiresAtUtc, issued.RefreshTokenExpiresAtUtc);
    }
}
