namespace Marketplace.Infrastructure.Services.Kyc;

/// <summary>
/// A single long-lived <see cref="HttpClient"/> for the NDID transport (mirrors M1's NotificationHttpClient):
/// registered as a singleton so we reuse one socket pool without depending on Microsoft.Extensions.Http /
/// IHttpClientFactory. BaseAddress is set from Kyc:NdidBaseUrl when present (Mode=Ndid).
/// </summary>
internal sealed class KycHttpClient : IDisposable
{
    public HttpClient Client { get; }

    public KycHttpClient(KycOptions options)
    {
        Client = new HttpClient();
        if (!string.IsNullOrWhiteSpace(options.NdidBaseUrl) &&
            Uri.TryCreate(options.NdidBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
        {
            Client.BaseAddress = baseUri;
        }
    }

    public void Dispose() => Client.Dispose();
}
