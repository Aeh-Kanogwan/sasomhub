using Marketplace.Application.Auditing;
using Marketplace.Application.Common;
using Marketplace.Application.Configuration;
using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Configuration;

/// <summary>
/// M7 — FR-31 (B-04/G-4) admin read/edit of versioned config over the IMMUTABLE
/// <see cref="ConfigVersion"/> history.
///
/// LEGAL/DATA INVARIANT (must not regress): the config history is APPEND-ONLY. Every edit INSERTs a
/// brand-new <see cref="ConfigVersion"/> row carrying its own <c>EffectiveFromUtc</c> — we NEVER UPDATE
/// or DELETE an existing row (the DB unique index UQ_ConfigVer_KeyEffective + an append-only trigger
/// enforce this at the database layer; this service must not even try). Resolution at runtime stays with
/// <see cref="ConfigVersionResolver"/> ("value of key K at time T = latest EffectiveFromUtc &lt;= T"), so a
/// new version is NOT retroactive: cycles already charged keep their snapshotted price
/// (Membership.PaidAmountTHB, Y-06) and a future-dated version only takes effect from its instant onward.
///
/// Every appended version is written to dbo.AuditLogs (FR-24) in the SAME SaveChanges as the INSERT,
/// recording the previous effective value (before) and the new one (after).
/// </summary>
public sealed class ConfigAdminService : IConfigAdminService
{
    private readonly MarketplaceDbContext _db;
    private readonly ConfigVersionResolver _resolver;
    private readonly IAuditService _audit;
    private readonly ILogger<ConfigAdminService> _logger;

    public ConfigAdminService(
        MarketplaceDbContext db,
        ConfigVersionResolver resolver,
        IAuditService audit,
        ILogger<ConfigAdminService> logger)
    {
        _db = db;
        _resolver = resolver;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// One row per distinct config key, each showing the value currently in effect (resolved as of now
    /// via <see cref="ConfigVersionResolver"/>, i.e. latest EffectiveFromUtc &lt;= now — future-dated
    /// versions are intentionally NOT shown as current). Ordered by key for a stable admin overview.
    /// </summary>
    public async Task<Result<IReadOnlyList<ConfigVersionDto>>> GetCurrentAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var keys = await _db.ConfigVersions.AsNoTracking()
            .Select(c => c.ConfigKey)
            .Distinct()
            .OrderBy(k => k)
            .ToListAsync(ct);

        var current = new List<ConfigVersionDto>(keys.Count);
        foreach (var key in keys)
        {
            // The latest version effective at "now" — the same selection ConfigVersionResolver uses, so the
            // admin overview matches exactly what services read at runtime.
            var row = await _db.ConfigVersions.AsNoTracking()
                .Where(c => c.ConfigKey == key && c.EffectiveFromUtc <= now)
                .OrderByDescending(c => c.EffectiveFromUtc)
                .FirstOrDefaultAsync(ct);

            if (row is not null)
                current.Add(ToDto(row));
        }

        return Result<IReadOnlyList<ConfigVersionDto>>.Success(current);
    }

    /// <summary>Full append-only version history for one key, newest EffectiveFromUtc first (FR-31).</summary>
    public async Task<Result<IReadOnlyList<ConfigVersionDto>>> GetHistoryAsync(string configKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configKey))
            return Result<IReadOnlyList<ConfigVersionDto>>.Fail("Config key is required.");

        var key = configKey.Trim();
        var rows = await _db.ConfigVersions.AsNoTracking()
            .Where(c => c.ConfigKey == key)
            .OrderByDescending(c => c.EffectiveFromUtc)
            .ThenByDescending(c => c.ConfigVersionId)
            .ToListAsync(ct);

        IReadOnlyList<ConfigVersionDto> dtos = rows.Select(ToDto).ToList();
        return Result<IReadOnlyList<ConfigVersionDto>>.Success(dtos);
    }

    /// <summary>
    /// FR-31: append a NEW version row (immutable). Validates the key/value, rejects an
    /// <c>EffectiveFromUtc</c> that collides with an existing version of the same key
    /// (UQ_ConfigVer_KeyEffective), INSERTs the row, and audits before/after of the EFFECTIVE value.
    /// Never updates an existing row and never affects already-charged cycles.
    /// </summary>
    public async Task<Result<ConfigVersionDto>> SetAsync(SetConfigRequest request, CancellationToken ct = default)
    {
        if (request is null)
            return Result<ConfigVersionDto>.Fail("Request is required.");

        var key = (request.ConfigKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return Result<ConfigVersionDto>.Fail("Config key is required.");

        // Only let admins edit keys the platform actually knows about (price/credit/expiry/bank). This
        // avoids silently creating typo'd "orphan" keys that no resolver ever reads.
        if (!IsKnownKey(key))
            return Result<ConfigVersionDto>.Fail($"Unknown config key '{key}'.");

        var rawValue = request.Value?.Trim() ?? string.Empty;
        var valueError = ValidateValue(key, rawValue);
        if (valueError is not null)
            return Result<ConfigVersionDto>.Fail(valueError);

        if (request.ChangedByUserId == Guid.Empty)
            return Result<ConfigVersionDto>.Fail("ChangedByUserId is required.");

        // EffectiveFromUtc is stored as a UTC instant; normalise so a Local/Unspecified kind from the web
        // form does not silently shift the resolve window.
        var effectiveFrom = NormalizeUtc(request.EffectiveFromUtc);

        // Collision guard (mirrors UQ_ConfigVer_KeyEffective): two versions of one key may not share the
        // same effective instant, otherwise "latest EffectiveFromUtc <= T" would be ambiguous.
        var collides = await _db.ConfigVersions.AsNoTracking()
            .AnyAsync(c => c.ConfigKey == key && c.EffectiveFromUtc == effectiveFrom, ct);
        if (collides)
            return Result<ConfigVersionDto>.Fail(
                "A config version for this key with the same EffectiveFromUtc already exists. Choose a different effective time.");

        // Capture the value currently effective at the NEW version's effective instant for the audit
        // before/after (the value the system would otherwise have used at that moment).
        var beforeValue = await _resolver.GetValueAsync(key, effectiveFrom, ct);

        var version = new ConfigVersion
        {
            ConfigKey = key,
            Value = rawValue,
            EffectiveFromUtc = effectiveFrom,
            CreatedByUserId = request.ChangedByUserId,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.ConfigVersions.Add(version);   // APPEND only — never UPDATE an existing row.

        // FR-24: audit the appended version (before/after of the effective value). Committed in the same
        // SaveChanges as the INSERT so the row and its audit trail persist (or roll back) together.
        _audit.Write(
            action: "Config.VersionAppended",
            entityType: "ConfigVersion",
            entityId: key,
            actorUserId: request.ChangedByUserId,
            before: new { ConfigKey = key, Value = beforeValue },
            after: new
            {
                ConfigKey = key,
                version.Value,
                version.EffectiveFromUtc,
                version.Note,
            });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Config version appended: key={ConfigKey} value={Value} effectiveFrom={EffectiveFromUtc} by={Admin}.",
            key, rawValue, effectiveFrom, request.ChangedByUserId);

        return Result<ConfigVersionDto>.Success(ToDto(version));
    }

    /// <summary>True when the key is one the platform reads through the resolver (price/credit/expiry/bank).</summary>
    private static bool IsKnownKey(string key)
    {
        // Static keys are matched exactly; per-tier price keys ("MembershipTier.{code}.AnnualPriceTHB")
        // are matched by shape so a new tier code can be priced without a code change.
        if (key is ConfigKeys.MembershipTrialMonths
                 or ConfigKeys.ReferralRewardReferrer
                 or ConfigKeys.ReferralRewardReferred
                 or ConfigKeys.ReferralCreditExpiryDays
                 or ConfigKeys.CompanyBankName
                 or ConfigKeys.CompanyBankAccountNo
                 or ConfigKeys.CompanyBankAccountName
                 or ConfigKeys.CompanyPromptPayId)
            return true;

        return key.StartsWith("MembershipTier.", StringComparison.Ordinal)
            && key.EndsWith(".AnnualPriceTHB", StringComparison.Ordinal)
            && key.Length > "MembershipTier.".Length + ".AnnualPriceTHB".Length;
    }

    /// <summary>
    /// Per-key value validation. Money/credit keys must be a non-negative number that is &gt; 0 where a
    /// zero would be nonsensical (prices/expiry); free-text bank keys just need to be non-empty.
    /// Returns null when valid, otherwise the rejection reason.
    /// </summary>
    private static string? ValidateValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Value is required.";

        // Decimal, strictly positive: membership prices + referral credit amounts.
        if (IsPriceKey(key)
            || key is ConfigKeys.ReferralRewardReferrer or ConfigKeys.ReferralRewardReferred)
        {
            if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var dec))
                return $"Value for '{key}' must be a number.";
            if (dec <= 0)
                return $"Value for '{key}' must be greater than 0.";
            return null;
        }

        // Integer, strictly positive: trial length (months) + credit expiry (days).
        if (key is ConfigKeys.MembershipTrialMonths or ConfigKeys.ReferralCreditExpiryDays)
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var i))
                return $"Value for '{key}' must be a whole number.";
            if (i <= 0)
                return $"Value for '{key}' must be greater than 0.";
            return null;
        }

        // Bank/PromptPay keys are free-form strings — only non-empty is enforced (already checked above).
        return null;
    }

    private static bool IsPriceKey(string key)
        => key.StartsWith("MembershipTier.", StringComparison.Ordinal)
        && key.EndsWith(".AnnualPriceTHB", StringComparison.Ordinal);

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static ConfigVersionDto ToDto(ConfigVersion c) => new(
        c.ConfigVersionId,
        c.ConfigKey,
        c.Value,
        c.EffectiveFromUtc,
        c.CreatedByUserId,
        c.Note,
        c.CreatedAtUtc);
}
