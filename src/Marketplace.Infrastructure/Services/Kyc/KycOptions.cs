using Microsoft.Extensions.Configuration;

namespace Marketplace.Infrastructure.Services.Kyc;

/// <summary>
/// M2 configuration (bound from the "Kyc" config section). LEGAL #2 selects the provider:
///   Kyc:Mode = "Mock" (dev/test/sandbox, default) | "Ndid" (real NDID transport).
/// A startup hard-guard (see <c>ModuleRegistration.AddM2Kyc</c>) forbids Mode=Mock in Production.
/// </summary>
public sealed class KycOptions
{
    public const string SectionName = "Kyc";

    /// <summary>Provider selector. "Mock" or "Ndid" (case-insensitive). Default "Mock".</summary>
    public string Mode { get; set; } = KycMode.Mock;

    // ---- NDID transport (only used when Mode=Ndid) ----------------------------------------------
    /// <summary>NDID API base URL (sandbox or production gateway). Required when Mode=Ndid.</summary>
    public string? NdidBaseUrl { get; set; }

    /// <summary>
    /// Relying-party / API key for NDID. NEVER committed — supplied via env var / user-secrets / KeyVault.
    /// TODO(boss): provide the real NDID credential + sandbox endpoint before enabling Mode=Ndid.
    /// </summary>
    public string? NdidApiKey { get; set; }

    /// <summary>NDID relying-party node id (identifier of our platform on the NDID network).</summary>
    public string? NdidRelyingPartyId { get; set; }

    /// <summary>
    /// HMAC-SHA256 pepper used to hash the national id BEFORE storage (LEGAL #4 — never plain SHA256,
    /// never the raw number). Supplied as a secret. TODO(boss/DPO): provision this secret per environment.
    /// When null, the service does NOT store a NationalIdHash at all (minimal-data path).
    /// </summary>
    public string? NationalIdHashPepper { get; set; }

    /// <summary>
    /// Days to retain KYC sensitive data after verification (LEGAL #4 retention).
    /// Placeholder default — TODO(legal/DPO): confirm the real retention period; tie to membership lifetime.
    /// </summary>
    public int SensitiveDataRetentionDays { get; set; } = 365 * 5;

    /// <summary>KYC result validity in days; drives KycVerification.ExpiresAtUtc (re-verify after expiry).</summary>
    public int VerificationValidityDays { get; set; } = 365;

    /// <summary>
    /// Read a <see cref="KycOptions"/> from a config section using the <see cref="IConfiguration"/> indexer
    /// (no Microsoft.Extensions.Configuration.Binder dependency). Unset keys keep the property defaults.
    /// </summary>
    public static KycOptions FromConfiguration(IConfiguration section)
    {
        var o = new KycOptions();
        if (section[nameof(Mode)] is { Length: > 0 } mode) o.Mode = mode;
        o.NdidBaseUrl = section[nameof(NdidBaseUrl)] ?? o.NdidBaseUrl;
        o.NdidApiKey = section[nameof(NdidApiKey)] ?? o.NdidApiKey;
        o.NdidRelyingPartyId = section[nameof(NdidRelyingPartyId)] ?? o.NdidRelyingPartyId;
        o.NationalIdHashPepper = section[nameof(NationalIdHashPepper)] ?? o.NationalIdHashPepper;
        if (int.TryParse(section[nameof(SensitiveDataRetentionDays)], out var ret)) o.SensitiveDataRetentionDays = ret;
        if (int.TryParse(section[nameof(VerificationValidityDays)], out var val)) o.VerificationValidityDays = val;
        return o;
    }
}

/// <summary>Canonical Kyc:Mode values.</summary>
public static class KycMode
{
    public const string Mock = "Mock";
    public const string Ndid = "Ndid";
}
