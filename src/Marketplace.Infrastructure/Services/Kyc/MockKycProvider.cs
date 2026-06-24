using Marketplace.Application.Kyc;

namespace Marketplace.Infrastructure.Services.Kyc;

/// <summary>
/// M2 sandbox <see cref="IKycProvider"/>. No network calls — lets the whole KYC flow run in dev/test/CI
/// without a live NDID connection (selected by Kyc:Mode=Mock, the default). Always returns verified=true.
/// LEGAL #2: a startup hard-guard forbids this provider in Production.
///
/// Mock data is tagged so it is never mistaken for real proofing: ProviderKey="MOCK", the provider
/// reference is prefixed "MOCK-", and the callback's RawStatus is "MOCK". KycSensitiveData.IsMockData is
/// also set by KycService so a prod purge sweep can remove sandbox rows.
/// </summary>
public sealed class MockKycProvider : IKycProvider
{
    public const string ReferencePrefix = "MOCK-";

    /// <summary>The provider key value emitted by the mock provider; used to tag KycSensitiveData.IsMockData.</summary>
    public const string ProviderKeyValue = "MOCK";

    public string ProviderKey => ProviderKeyValue;

    public Task<StartKycResult> InitiateAsync(StartKycRequest request, CancellationToken ct = default)
    {
        // Deterministic-but-unique sandbox reference, clearly tagged as mock.
        var reference = ReferencePrefix + Guid.NewGuid().ToString("N");
        var result = new StartKycResult(
            KycVerificationId: Guid.Empty,            // assigned by KycService after it persists the row
            Provider: ProviderKey,
            RedirectUrl: null,                        // no real handoff in mock mode
            ProviderReference: reference);
        return Task.FromResult(result);
    }

    public Task<KycCallback> CheckStatusAsync(string providerReference, CancellationToken ct = default)
    {
        // Sandbox always approves. No raw national id is produced (we never store real PII in mock).
        var callback = new KycCallback(
            ProviderReference: providerReference,
            Verified: true,
            FullNameMasked: "MOCK USER",
            NationalIdHash: null,                     // mock never yields a real id to hash
            RawStatus: "MOCK");
        return Task.FromResult(callback);
    }
}
