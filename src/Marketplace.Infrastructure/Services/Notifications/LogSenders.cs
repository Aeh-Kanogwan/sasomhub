using Marketplace.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// DEV fallback email transport: does NOT contact any provider — it logs that an email "would" be
/// sent and reports success so the full pipeline (incl. delivery-log persistence) runs locally with
/// zero credentials. NEVER logs the raw recipient at Information level (PDPA); the body length only.
/// </summary>
internal sealed class LogEmailSender : IEmailSender
{
    public const string ProviderKey = "LogEmail";
    private readonly ILogger<LogEmailSender> _logger;

    public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;

    public Task<SendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[EMAIL:Log] would send subject='{Subject}' bodyLen={BodyLen} (recipient redacted).",
            subject, htmlBody?.Length ?? 0);
        var receipt = $"log-{Guid.NewGuid():N}";
        return Task.FromResult(new SendResult(true, ProviderKey, receipt, null));
    }
}

/// <summary>DEV fallback SMS transport — see <see cref="LogEmailSender"/>.</summary>
internal sealed class LogSmsSender : ISmsSender
{
    public const string ProviderKey = "LogSms";
    private readonly ILogger<LogSmsSender> _logger;

    public LogSmsSender(ILogger<LogSmsSender> logger) => _logger = logger;

    public Task<SendResult> SendSmsAsync(string toPhone, string message, CancellationToken ct = default)
    {
        _logger.LogInformation("[SMS:Log] would send msgLen={MsgLen} (recipient redacted).", message?.Length ?? 0);
        var receipt = $"log-{Guid.NewGuid():N}";
        return Task.FromResult(new SendResult(true, ProviderKey, receipt, null));
    }
}
