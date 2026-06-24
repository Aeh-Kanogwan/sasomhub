using System.Net;
using System.Net.Mail;
using Marketplace.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// SMTP email transport using the BCL <see cref="SmtpClient"/> (no extra NuGet). Stateless w.r.t. the
/// DB: it only hands the message to the SMTP relay and returns a <see cref="SendResult"/>; the
/// <see cref="NotificationSender"/> facade owns delivery-log persistence. Any transport failure is
/// captured as a non-throwing failed result (Accepted=false) so the facade can log Status=Failed.
/// </summary>
internal sealed class SmtpEmailSender : IEmailSender
{
    public const string ProviderKey = "Smtp";

    private readonly SmtpOptions _options;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(NotificationOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Smtp;
        _fromEmail = options.FromEmail;
        _fromName = options.FromName;
        _logger = logger;
    }

    public async Task<SendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_fromEmail, _fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
            };
            message.To.Add(toEmail);

            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.EnableSsl,
                Timeout = _options.TimeoutMs,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };
            if (!string.IsNullOrEmpty(_options.Username))
                client.Credentials = new NetworkCredential(_options.Username, _options.Password);

            // SmtpClient has no native CancellationToken overload; honour an already-cancelled token.
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, ct);

            // SMTP gives no per-message receipt id; synthesise a local correlation id for the log.
            return new SendResult(true, ProviderKey, $"smtp-{Guid.NewGuid():N}", null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EMAIL:Smtp] send failed via {Host}:{Port}.", _options.Host, _options.Port);
            return new SendResult(false, ProviderKey, null, Truncate(ex.Message));
        }
    }

    private static string Truncate(string s) => s.Length <= 900 ? s : s[..900];
}
