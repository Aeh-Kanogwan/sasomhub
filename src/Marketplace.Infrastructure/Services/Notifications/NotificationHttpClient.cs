namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// A single long-lived <see cref="HttpClient"/> shared by the HTTP-based M1 providers (SendGrid email,
/// SMS gateway). Registered as a singleton so we reuse one socket pool without taking a dependency on
/// Microsoft.Extensions.Http / IHttpClientFactory (keeps the Infrastructure package set unchanged).
/// </summary>
internal sealed class NotificationHttpClient : IDisposable
{
    public HttpClient Client { get; } = new HttpClient();

    public void Dispose() => Client.Dispose();
}
