using System.Net.Http.Headers;
using System.Net.Http.Json;
using Marketplace.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// Generic HTTP SMS gateway transport (no provider SDK NuGet). POSTs <c>{to, message}</c> as JSON to
/// the configured gateway URL with the API key in the configured header. Stateless w.r.t. the DB —
/// returns a <see cref="SendResult"/>; the facade persists the delivery log. Adapt the payload shape
/// to the chosen Thai SMS gateway once a real endpoint is provided.
/// </summary>
internal sealed class SmsHttpSender : ISmsSender
{
    public const string ProviderKey = "SmsHttpGateway";

    private readonly HttpClient _http;
    private readonly SmsGatewayOptions _options;
    private readonly ILogger<SmsHttpSender> _logger;

    public SmsHttpSender(HttpClient http, NotificationOptions options, ILogger<SmsHttpSender> logger)
    {
        _http = http;
        _options = options.Sms;
        _logger = logger;
    }

    public async Task<SendResult> SendSmsAsync(string toPhone, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
            return new SendResult(false, ProviderKey, null, "SMS gateway Url not configured.");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _options.Url)
            {
                Content = JsonContent.Create(new { to = toPhone, message }),
            };
            ApplyApiKey(req);

            using var resp = await _http.SendAsync(req, ct);
            var body = await SafeReadAsync(resp, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[SMS:Http] HTTP {Status}: {Body}", (int)resp.StatusCode, body);
                return new SendResult(false, ProviderKey, null, Truncate($"HTTP {(int)resp.StatusCode}: {body}"));
            }

            // Gateways vary; surface the body as the receipt id (trimmed) for reconciliation.
            var receipt = string.IsNullOrWhiteSpace(body) ? $"sms-{Guid.NewGuid():N}" : Truncate(body, 200);
            return new SendResult(true, ProviderKey, receipt, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SMS:Http] send failed.");
            return new SendResult(false, ProviderKey, null, Truncate(ex.Message));
        }
    }

    private void ApplyApiKey(HttpRequestMessage req)
    {
        if (string.IsNullOrEmpty(_options.ApiKey)) return;
        if (string.Equals(_options.ApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        else
            req.Headers.TryAddWithoutValidation(_options.ApiKeyHeader, _options.ApiKey);
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private static string Truncate(string s, int max = 900) => s.Length <= max ? s : s[..max];
}
