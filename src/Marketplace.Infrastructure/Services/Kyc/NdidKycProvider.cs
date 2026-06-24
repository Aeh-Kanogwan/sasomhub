using System.Net.Http.Json;
using Marketplace.Application.Kyc;

namespace Marketplace.Infrastructure.Services.Kyc;

/// <summary>
/// M2 real <see cref="IKycProvider"/>: talks to NDID over HTTP (selected by Kyc:Mode=Ndid).
///
/// SKELETON ONLY — we do not yet have a live NDID credential or sandbox endpoint. The transport shape,
/// config wiring and DTO mapping are in place; every spot that needs a real secret/endpoint contract is
/// marked TODO(ndid). It NEVER reads/stores raw ID images or numbers (LEGAL #4): the provider returns a
/// masked name + (optionally) an already-hashed national id reference, plus a provider reference.
///
/// Build/test never require a credential: nothing here runs unless Mode=Ndid is configured AND a request
/// is actually made. The startup guard ensures Mode=Ndid in Production is wired with a base URL.
/// </summary>
public sealed class NdidKycProvider : IKycProvider
{
    private readonly HttpClient _http;
    private readonly KycOptions _options;

    public NdidKycProvider(HttpClient http, KycOptions options)
    {
        _http = http;
        _options = options;
    }

    public string ProviderKey => "NDID";

    public async Task<StartKycResult> InitiateAsync(StartKycRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();

        // TODO(ndid): replace path + payload with the real NDID relying-party "create request" contract.
        //             Add the auth header from _options.NdidApiKey / signed request per NDID spec.
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/identity/requests");
        ApplyAuth(httpRequest);
        httpRequest.Content = JsonContent.Create(new
        {
            relyingPartyId = _options.NdidRelyingPartyId,
            // NDID proofs an existing identity; we send only the minimum needed to start a request.
            // No raw national id / image is sent from here (collected by the user at the IdP, LEGAL #4).
            referenceId = request.UserId.ToString("N"),
            minIal = request.TargetLevel == KycLevel.Premium ? 3 : 2, // map tier -> NDID identity assurance level
        });

        var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        // TODO(ndid): bind to the real NDID response schema (request id, redirect/QR url).
        var dto = await response.Content.ReadFromJsonAsync<NdidInitiateResponse>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("NDID: empty initiate response.");

        return new StartKycResult(
            KycVerificationId: Guid.Empty,            // assigned by KycService
            Provider: ProviderKey,
            RedirectUrl: dto.RedirectUrl,
            ProviderReference: dto.RequestId);
    }

    public async Task<KycCallback> CheckStatusAsync(string providerReference, CancellationToken ct = default)
    {
        EnsureConfigured();

        // TODO(ndid): replace with the real NDID "get request status / result" endpoint.
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"v1/identity/requests/{providerReference}");
        ApplyAuth(httpRequest);

        var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<NdidStatusResponse>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("NDID: empty status response.");

        // LEGAL #4: NEVER persist the raw national id. If NDID returns identifying data, the hashing
        // happens in KycService using the HMAC pepper; here we only surface a masked name + raw status.
        return new KycCallback(
            ProviderReference: providerReference,
            Verified: string.Equals(dto.Status, "completed", StringComparison.OrdinalIgnoreCase),
            FullNameMasked: dto.FullNameMasked,
            NationalIdHash: null,                     // KycService hashes if/when a raw value is available
            RawStatus: dto.Status);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.NdidBaseUrl))
            throw new InvalidOperationException(
                "NDID is not configured: set Kyc:NdidBaseUrl (and Kyc:NdidApiKey/Kyc:NdidRelyingPartyId). " +
                "TODO(boss): supply the real NDID credential + sandbox endpoint.");
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        // TODO(ndid): NDID uses signed requests / API keys — replace this placeholder bearer scheme with
        //             the real auth (request signing) once credentials are provisioned.
        if (!string.IsNullOrWhiteSpace(_options.NdidApiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.NdidApiKey}");
    }

    // ---- Placeholder NDID DTOs (shapes are TODO until the real schema is confirmed) -----------------
    private sealed record NdidInitiateResponse(string RequestId, string? RedirectUrl);
    private sealed record NdidStatusResponse(string Status, string? FullNameMasked);
}
