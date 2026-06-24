using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.Application.Notifications;
using Marketplace.Domain.Entities;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// M1 facade (INotificationSender). Routes a <see cref="SendMessageRequest"/> to the channel sender,
/// renders the template, calls the provider, and PERSISTS a <see cref="NotificationDeliveryLog"/> row
/// on EVERY attempt — success or failure (LEGAL #8 evidence). Append-only: each attempt is a NEW row
/// under the same <see cref="SendMessageRequest.CorrelationId"/> (never an UPDATE).
///
/// PDPA: the log stores a MASKED recipient + a keyed HMAC fingerprint in the first-class
/// <see cref="NotificationDeliveryLog.RecipientHash"/> column — never the raw email/phone. The rendered
/// template VARIABLES are snapshotted (not the full HTML) so the exact message can be reconstructed from
/// TemplateKey + TemplateVersion + variables.
/// </summary>
internal sealed class NotificationSender : INotificationSender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MarketplaceDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationSender> _logger;

    public NotificationSender(
        MarketplaceDbContext db,
        IEmailSender emailSender,
        ISmsSender smsSender,
        NotificationOptions options,
        ILogger<NotificationSender> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<SendResult>> SendAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        if (request is null) return Result<SendResult>.Fail("Request is null.");
        if (string.IsNullOrWhiteSpace(request.Recipient))
            return Result<SendResult>.Fail("Recipient is required.");

        var channelKind = request.Channel == SendChannel.Email ? SendChannelKind.Email : SendChannelKind.Sms;
        var deliveryChannel = request.Channel == SendChannel.Email ? DeliveryChannel.Email : DeliveryChannel.Sms;

        var masked = RecipientPrivacy.Mask(channelKind, request.Recipient);
        var recipientHash = RecipientPrivacy.Hash(channelKind, request.Recipient, _options.RecipientHashPepper);

        var (subject, body) = Render(request);

        SendResult result;
        try
        {
            result = request.Channel == SendChannel.Email
                ? await _emailSender.SendEmailAsync(request.Recipient, subject, body, ct)
                : await _smsSender.SendSmsAsync(request.Recipient, body, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A transport that throws instead of returning a failed result still produces evidence.
            _logger.LogError(ex, "M1 send threw for template {TemplateKey} on {Channel}.", request.TemplateKey, request.Channel);
            result = new SendResult(false, ProviderKeyFor(request.Channel), null, ex.Message);
        }

        var now = DateTime.UtcNow;
        var log = new NotificationDeliveryLog
        {
            NotificationId = request.NotificationId,
            UserId = request.UserId,
            Provider = result.Provider,
            ProviderMessageId = result.ProviderMessageId,
            Channel = deliveryChannel,
            RecipientMasked = masked,
            RecipientHash = recipientHash,
            TemplateKey = request.TemplateKey,
            TemplateVersion = request.TemplateVersion,
            Status = result.Accepted ? DeliveryStatus.Sent : DeliveryStatus.Failed,
            ErrorDetail = Truncate(result.Error),
            AttemptCount = 0,
            PayloadSnapshotJson = BuildSnapshot(request.Variables),
            CorrelationId = request.CorrelationId,
            SentAtUtc = result.Accepted ? now : null,
            CreatedAtUtc = now,
            RetentionExpiresAtUtc = now.AddYears(_options.RetentionYears),
        };

        _db.NotificationDeliveryLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        return result.Accepted
            ? Result<SendResult>.Success(result)
            : Result<SendResult>.Fail(result.Error ?? "Provider rejected the message.");
    }

    /// <summary>
    /// Minimal template renderer: there is no template store yet, so we build a subject/body from the
    /// TemplateKey + merge variables. Replaced when a real template repository lands; the delivery log
    /// already captures TemplateKey+Version+vars so the exact wording is reconstructable either way.
    /// </summary>
    private static (string Subject, string Body) Render(SendMessageRequest request)
    {
        var subject = request.Variables.TryGetValue("subject", out var s) && !string.IsNullOrWhiteSpace(s)
            ? s
            : request.TemplateKey;

        var lines = request.Variables
            .Where(kv => !string.Equals(kv.Key, "subject", StringComparison.OrdinalIgnoreCase))
            .Select(kv => $"{kv.Key}: {kv.Value}");
        var body = $"[{request.TemplateKey}@{request.TemplateVersion}]\n" + string.Join("\n", lines);
        return (subject, body);
    }

    private static string BuildSnapshot(IReadOnlyDictionary<string, string> variables)
        => JsonSerializer.Serialize(variables, JsonOptions);

    private string ProviderKeyFor(SendChannel channel) => channel == SendChannel.Email
        ? _options.EmailProvider.ToString()
        : _options.SmsProvider.ToString();

    private static string? Truncate(string? s)
        => s is null ? null : (s.Length <= 1000 ? s : s[..1000]);
}
