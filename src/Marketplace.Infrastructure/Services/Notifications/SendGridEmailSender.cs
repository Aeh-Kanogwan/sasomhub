using System.Net.Http.Headers;
using System.Net.Http.Json;
using Marketplace.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// SendGrid email transport via a raw HTTPS call (no SendGrid SDK NuGet). Posts the v3 mail/send
/// payload with a Bearer API key. Stateless w.r.t. the DB — returns a <see cref="SendResult"/>;
/// the facade persists the delivery log. The provider's <c>X-Message-Id</c> response header is
/// captured as the receipt id for reconciliation.
/// </summary>
internal sealed class SendGridEmailSender : IEmailSender
{
    public const string ProviderKey = "SendGrid";

    private readonly HttpClient _http;
    private readonly SendGridOptions _options;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(HttpClient http, NotificationOptions options, ILogger<SendGridEmailSender> logger)
    {
        _http = http;
        _options = options.SendGrid;
        _fromEmail = options.FromEmail;
        _fromName = options.FromName;
        _logger = logger;
    }

    public async Task<SendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return new SendResult(false, ProviderKey, null, "SendGrid ApiKey not configured.");

        try
        {
            var payload = new
            {
                personalizations = new[] { new { to = new[] { new { email = toEmail } } } },
                from = new { email = _fromEmail, name = _fromName },
                subject,
                content = new[] { new { type = "text/html", value = htmlBody } },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _options.ApiBaseUrl)
            {
                Content = JsonContent.Create(payload),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(resp, ct);
                _logger.LogWarning("[EMAIL:SendGrid] HTTP {Status}: {Body}", (int)resp.StatusCode, body);
                return new SendResult(false, ProviderKey, null, Truncate($"HTTP {(int)resp.StatusCode}: {body}"));
            }

            var messageId = resp.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.FirstOrDefault() : null;
            return new SendResult(true, ProviderKey, messageId, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EMAIL:SendGrid] send failed.");
            return new SendResult(false, ProviderKey, null, Truncate(ex.Message));
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return "(no body)"; }
    }

    private static string Truncate(string s) => s.Length <= 900 ? s : s[..900];
}
