using System.Security.Claims;
using Marketplace.Application.Common;
using Marketplace.Application.Memberships;
using Marketplace.Application.Referrals;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services;
using Marketplace.Web.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Web.Controllers;

/// <summary>
/// FR-01/FR-27/FR-28/FR-16/FR-15: register (PDPA consent + optional referral + 3-month trial),
/// login/logout (cookie auth), the collector profile, and the appeal (due-process) entry point.
/// </summary>
public class AccountController : Controller
{
    private readonly MarketplaceDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMembershipService _membershipService;
    private readonly IReferralService _referralService;
    private readonly IReferralCodeGenerator _referralCodeGenerator;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        MarketplaceDbContext db,
        IPasswordHasher passwordHasher,
        IMembershipService membershipService,
        IReferralService referralService,
        IReferralCodeGenerator referralCodeGenerator,
        ILogger<AccountController> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _membershipService = membershipService;
        _referralService = referralService;
        _referralCodeGenerator = referralCodeGenerator;
        _logger = logger;
    }

    // ---- Register (FR-01 / FR-27 / FR-28) ----

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
    {
        // FR-01: PDPA consent is enforced by [Range(true,true)] on ConsentPdpa; double-check defensively.
        if (!model.ConsentPdpa)
            ModelState.AddModelError(nameof(model.ConsentPdpa), "ต้องยอมรับนโยบาย PDPA และเงื่อนไขการใช้งานก่อนสมัคร");
        if (!ModelState.IsValid)
            return View(model);

        var normalizedEmail = model.Email.Trim().ToUpperInvariant();

        // Reject duplicate email up front (the DB also enforces UX_Users_NormalizedEmail WHERE IsDeleted=0).
        var emailTaken = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted, ct);
        if (emailTaken)
        {
            ModelState.AddModelError(nameof(model.Email), "อีเมลนี้ถูกใช้งานแล้ว");
            return View(model);
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = model.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(model.Password),   // NFR-S1: store hash only, never plaintext
            PhoneNumber = model.PhoneNumber.Trim(),
            Role = UserRole.Member,
            AccountStatus = AccountStatus.Active,
            EmailConfirmed = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Profile = new UserProfile
            {
                // Display name defaults to the email local-part until the user edits their profile.
                DisplayName = DeriveDisplayName(model.Email),
                UpdatedAtUtc = now,
            },
            TrustScore = new TrustScore { Score = 100, LastCalculatedAtUtc = now },
        };
        _db.Users.Add(user);

        // FR-01: append-only PDPA consent record (IP hash omitted here — captured at infra/proxy layer).
        _db.ConsentRecords.Add(new ConsentRecord
        {
            User = user,
            ConsentType = "PDPA",
            DocumentVersion = "v1",
            IsGranted = true,
            CreatedAtUtc = now,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Lost the race on UX_Users_NormalizedEmail (or another constraint) — fail closed, no half-state.
            _logger.LogWarning(ex, "Register persistence failed for {Email}", normalizedEmail);
            ModelState.AddModelError(string.Empty, "ไม่สามารถสมัครได้ในขณะนี้ กรุณาลองใหม่อีกครั้ง");
            return View(model);
        }

        // FR-27: start the 3-month free trial on the Normal tier (id=1). Non-fatal if it fails.
        var trial = await _membershipService.StartTrialAsync(
            new StartTrialRequest(user.UserId, MembershipTierId: 1), ct);
        if (!trial.Succeeded)
            _logger.LogWarning("StartTrialAsync failed for {UserId}: {Error}", user.UserId, trial.Error);

        // FR-28 (single-level): give this new user their OWN active referral code so the "invite friends"
        // CTA on /Credits has a code to show. Non-fatal: a generation/persist failure must never break
        // registration (the Credits page degrades to a disabled button when no code exists).
        await CreateOwnReferralCodeAsync(user.UserId, ct);

        // FR-28: optional single-level referral. Invalid/duplicate codes must NOT block registration.
        if (!string.IsNullOrWhiteSpace(model.ReferralCode))
        {
            var referral = await _referralService.RegisterAsync(
                new RegisterReferralRequest(user.UserId, model.ReferralCode.Trim()), ct);
            if (!referral.Succeeded)
                _logger.LogInformation("Referral link skipped for {UserId}: {Error}", user.UserId, referral.Error);
        }

        await SignInWithMembershipAsync(user, isPersistent: true, ct);
        return RedirectToAction(nameof(Profile));
    }

    // ---- Login / Logout (FR-01) ----

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        var normalizedEmail = model.Email.Trim().ToUpperInvariant();
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted, ct);

        // Generic error on any failure (no user enumeration). Always run a verify to keep timing uniform.
        var hashToCheck = user?.PasswordHash;
        var passwordOk = hashToCheck is not null && _passwordHasher.Verify(model.Password, hashToCheck);

        if (user is null || !passwordOk)
        {
            ModelState.AddModelError(string.Empty, "อีเมลหรือรหัสผ่านไม่ถูกต้อง");
            return View(model);
        }

        if (user.AccountStatus is AccountStatus.Suspended or AccountStatus.Banned)
        {
            ModelState.AddModelError(string.Empty, "บัญชีนี้ถูกระงับการใช้งาน กรุณาติดต่อฝ่ายสนับสนุน");
            return View(model);
        }

        await SignInWithMembershipAsync(user, model.RememberMe, ct);

        if (Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl!);
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    // ---- Profile (FR-16 / FR-27 / FR-15 / FR-17) ----

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
            return Challenge();

        // Profile + trust score + current membership. Degrade gracefully if the DB is empty/unavailable.
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Profile)
            .Include(u => u.TrustScore)
            .FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);

        var membership = await _db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId.Value)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new
            {
                m.Status,
                m.TrialEndsAtUtc,
                m.PaidThroughUtc,
                TierLabel = m.MembershipTier.DisplayName,
            })
            .FirstOrDefaultAsync(ct);

        var history = await _db.TrustScoreHistory.AsNoTracking()
            .Where(h => h.UserId == userId.Value)
            .OrderByDescending(h => h.CreatedAtUtc)
            .Take(20)
            .Select(h => new TrustScoreHistoryRow(
                h.CreatedAtUtc, h.Delta, h.ScoreAfter, h.ReasonCode!.DisplayName, h.Note))
            .ToListAsync(ct);

        var collectionCount = await _db.Products.AsNoTracking()
            .CountAsync(p => p.SellerId == userId.Value, ct);

        var completedDeals = await _db.Transactions.AsNoTracking()
            .CountAsync(t => (t.SellerId == userId.Value || t.BuyerId == userId.Value)
                             && t.Status == TransactionStatus.Confirmed, ct);

        // FR-15: an active, confirmed blacklist entry that has not yet been appealed is appealable.
        var appealable = await _db.BlacklistEntries.AsNoTracking()
            .Where(e => e.UserId == userId.Value && e.IsActive
                        && e.AppealStatus == BlacklistAppealStatus.None)
            .OrderByDescending(e => e.EffectiveAtUtc)
            .Select(e => (Guid?)e.BlacklistEntryId)
            .FirstOrDefaultAsync(ct);

        var vm = new ProfileViewModel
        {
            DisplayName = user?.Profile?.DisplayName ?? User.Identity?.Name ?? "นักสะสม",
            IsVerified = membership?.TierLabel is "Verified" or "Premium",
            TrustScore = user?.TrustScore?.Score ?? 100,
            CompletedDeals = completedDeals,
            CollectionCount = collectionCount,
            MembershipStatus = membership?.Status ?? MembershipStatus.Expired,
            MembershipTierLabel = membership?.TierLabel ?? string.Empty,
            TrialEndsAtUtc = membership?.TrialEndsAtUtc,
            PaidThroughUtc = membership?.PaidThroughUtc,
            TrustScoreHistory = history,
            CanAppeal = appealable.HasValue,
            AppealableEntryId = appealable ?? Guid.Empty,
        };
        return View(vm);
    }

    // ---- Appeal (FR-15, due process) ----

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Appeal(AppealViewModel model, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
            return Challenge();

        if (!ModelState.IsValid)
        {
            TempData["AppealError"] = "กรุณาระบุเหตุผลในการอุทธรณ์ (อย่างน้อย 10 ตัวอักษร)";
            return RedirectToAction(nameof(Profile));
        }

        // Only the owner of an active, not-yet-appealed entry may appeal it (due-process gate).
        var entry = await _db.BlacklistEntries
            .FirstOrDefaultAsync(e => e.BlacklistEntryId == model.BlacklistEntryId
                                      && e.UserId == userId.Value
                                      && e.IsActive, ct);
        if (entry is null)
        {
            TempData["AppealError"] = "ไม่พบรายการที่สามารถยื่นอุทธรณ์ได้";
            return RedirectToAction(nameof(Profile));
        }

        if (entry.AppealStatus != BlacklistAppealStatus.None)
        {
            TempData["AppealError"] = "รายการนี้อยู่ระหว่างการพิจารณาอุทธรณ์อยู่แล้ว";
            return RedirectToAction(nameof(Profile));
        }

        // Record the appeal for human review — we never auto-resolve (DP: no automated final decision).
        entry.AppealNote = model.AppealNote.Trim();
        entry.AppealStatus = BlacklistAppealStatus.Requested;
        entry.ReviewStatus = BlacklistReviewStatus.Appealed;
        await _db.SaveChangesAsync(ct);

        TempData["AppealOk"] = "ส่งคำอุทธรณ์เรียบร้อยแล้ว ทีมงานจะตรวจสอบและแจ้งผลให้ทราบ";
        return RedirectToAction(nameof(Profile));
    }

    // ---- Helpers ----

    /// <summary>
    /// FR-28 (single-level): create exactly one active ReferralCode for the user. Retries on the rare unique
    /// collision (UQ_RefCode_Code); never throws — registration must succeed even if this step fails. Skips
    /// silently if the user somehow already has a code (UQ_RefCode_User keeps it to one per user).
    /// </summary>
    private async Task CreateOwnReferralCodeAsync(Guid userId, CancellationToken ct)
    {
        const int maxInsertAttempts = 3;

        try
        {
            var alreadyHas = await _db.ReferralCodes.AsNoTracking().AnyAsync(c => c.UserId == userId, ct);
            if (alreadyHas)
                return;

            for (var attempt = 0; attempt < maxInsertAttempts; attempt++)
            {
                var code = await _referralCodeGenerator.GenerateUniqueAsync(ct);
                var entity = new ReferralCode
                {
                    UserId = userId,
                    Code = code,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                _db.ReferralCodes.Add(entity);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    return; // success
                }
                catch (DbUpdateException ex)
                {
                    // Lost a race on UQ_RefCode_Code (duplicate Code) or UQ_RefCode_User (code already created
                    // concurrently). Detach the failed entity and either retry with a fresh code, or stop if the
                    // user already has one now.
                    _db.Entry(entity).State = EntityState.Detached;
                    if (await _db.ReferralCodes.AsNoTracking().AnyAsync(c => c.UserId == userId, ct))
                        return; // a concurrent request already created the user's code
                    _logger.LogInformation(ex, "Referral code insert collided for {UserId}; retrying", userId);
                }
            }

            _logger.LogWarning("Could not create a referral code for {UserId} after {Attempts} attempts",
                userId, maxInsertAttempts);
        }
        catch (Exception ex)
        {
            // Graceful degrade: DB unavailable / unexpected error. The Credits CTA simply stays disabled.
            _logger.LogWarning(ex, "Referral code creation skipped for {UserId}", userId);
        }
    }

    /// <summary>
    /// FR-27 / _Layout contract: build the cookie principal with the claims the layout and policies read —
    /// Name (display), MembershipTier, MembershipStatus, and the MembershipActive gate (FR-06/FR-10).
    /// </summary>
    private async Task SignInWithMembershipAsync(User user, bool isPersistent, CancellationToken ct)
    {
        var membership = await _db.Memberships.AsNoTracking()
            .Where(m => m.UserId == user.UserId)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new { m.Status, Tier = m.MembershipTier.DisplayName })
            .FirstOrDefaultAsync(ct);

        var isActive = await _membershipService.IsMembershipActiveAsync(user.UserId, ct);
        var displayName = user.Profile?.DisplayName
            ?? await _db.UserProfiles.AsNoTracking()
                .Where(p => p.UserId == user.UserId)
                .Select(p => p.DisplayName)
                .FirstOrDefaultAsync(ct)
            ?? DeriveDisplayName(user.Email);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("MembershipActive", isActive ? "true" : "false"),
        };
        if (membership is not null)
        {
            claims.Add(new Claim("MembershipTier", membership.Tier));
            claims.Add(new Claim("MembershipStatus", membership.Status.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = isPersistent });
    }

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static string DeriveDisplayName(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "นักสะสม" : local;
    }
}
