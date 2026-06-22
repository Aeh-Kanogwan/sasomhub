using Marketplace.Domain.Entities;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// B-04/G-4 (FR-31/FR-35): resolves an admin-editable config value as of a point in time from the
/// immutable <see cref="ConfigVersion"/> history ("value of key K at time T = latest EffectiveFromUtc &lt;= T").
/// Prices/credit amounts/expiry are read through here so nothing is hardcoded in service logic.
/// The MembershipTiers/PromotionPackages lookups remain the "current" convenience copy; this resolver
/// is the source of truth for the value that gets snapshotted onto a paid cycle.
/// </summary>
public sealed class ConfigVersionResolver
{
    private readonly MarketplaceDbContext _db;

    public ConfigVersionResolver(MarketplaceDbContext db) => _db = db;

    /// <summary>Latest config value for <paramref name="key"/> effective at <paramref name="asOfUtc"/>, or null if unset.</summary>
    public async Task<string?> GetValueAsync(string key, DateTime asOfUtc, CancellationToken ct = default)
    {
        return await _db.ConfigVersions
            .Where(c => c.ConfigKey == key && c.EffectiveFromUtc <= asOfUtc)
            .OrderByDescending(c => c.EffectiveFromUtc)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Decimal config value effective at <paramref name="asOfUtc"/>, or <paramref name="fallback"/> if unset/unparseable.</summary>
    public async Task<decimal> GetDecimalAsync(string key, DateTime asOfUtc, decimal fallback, CancellationToken ct = default)
    {
        var raw = await GetValueAsync(key, asOfUtc, ct);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    /// <summary>Int config value effective at <paramref name="asOfUtc"/>, or <paramref name="fallback"/> if unset/unparseable.</summary>
    public async Task<int> GetIntAsync(string key, DateTime asOfUtc, int fallback, CancellationToken ct = default)
    {
        var raw = await GetValueAsync(key, asOfUtc, ct);
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}

/// <summary>Canonical ConfigVersions keys used by the growth/membership services (mirrors schema.sql comment format).</summary>
public static class ConfigKeys
{
    // FR-27: annual membership price per tier. Resolved by tier Code, e.g. "MembershipTier.Premium.AnnualPriceTHB".
    public static string MembershipAnnualPrice(string tierCode) => $"MembershipTier.{tierCode}.AnnualPriceTHB";

    // FR-27: free-trial length in months (default 3).
    public const string MembershipTrialMonths = "Membership.TrialMonths";

    // FR-28: referral reward credit amounts (referrer vs referred) and the credit expiry window in days.
    public const string ReferralRewardReferrer = "Referral.RewardCreditToReferrer";
    public const string ReferralRewardReferred = "Referral.RewardCreditToReferred";
    public const string ReferralCreditExpiryDays = "Referral.CreditExpiryDays";

    // Flow B (FR-22/FR-35): company bank account that members transfer the membership fee into.
    // Values are admin-editable via ConfigVersions (placeholder seed until business sets the real account).
    public const string CompanyBankName = "Company.BankName";
    public const string CompanyBankAccountNo = "Company.BankAccountNo";
    public const string CompanyBankAccountName = "Company.BankAccountName";
    public const string CompanyPromptPayId = "Company.PromptPayId";
}
