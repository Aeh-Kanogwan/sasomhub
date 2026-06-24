using Marketplace.Application.Common;

namespace Marketplace.Application.Notifications;

// M1: real Email/SMS delivery. Provider-agnostic abstraction so we can swap SendGrid/SES/Twilio/etc.
// without touching callers. EVERY send MUST persist a dbo.NotificationDeliveryLog row (LEGAL #8 proof).
// Implementations: backend-dev (M1) creates one IEmailSender impl, one ISmsSender impl, and one
// INotificationSender facade that picks the channel, renders the template, and writes the delivery log.

/// <summary>Outbound channel for a real provider send (mirrors Domain.Enums.DeliveryChannel).</summary>
public enum SendChannel { Email, Sms }

/// <summary>
/// A request to send one templated message. <paramref name="Recipient"/> is the RAW address
/// (email/phone) — the sender masks it before logging. <paramref name="Variables"/> are the
/// template merge fields (no secrets). TemplateKey+TemplateVersion identify the exact wording.
/// </summary>
public record SendMessageRequest(
    SendChannel Channel,
    string Recipient,
    string TemplateKey,
    string TemplateVersion,
    IReadOnlyDictionary<string, string> Variables,
    Guid? UserId = null,
    Guid? NotificationId = null,
    Guid? CorrelationId = null);

/// <summary>Result of a provider send. ProviderMessageId is the receipt id for reconciliation.</summary>
public record SendResult(bool Accepted, string Provider, string? ProviderMessageId, string? Error);

/// <summary>
/// M1 facade — routes a <see cref="SendMessageRequest"/> to the right channel sender, renders the
/// template, calls the provider, and PERSISTS a NotificationDeliveryLog row (Queued -> Sent/Failed,
/// retries append new rows under the same CorrelationId). This is what callers (Worker/services) use.
/// </summary>
public interface INotificationSender
{
    /// <summary>Send one templated message via its channel and persist the delivery log (LEGAL #8).</summary>
    Task<Result<SendResult>> SendAsync(SendMessageRequest request, CancellationToken ct = default);
}

/// <summary>
/// M1: concrete email transport (SMTP/SendGrid/SES). Stateless w.r.t. the DB — it ONLY talks to the
/// provider and returns a <see cref="SendResult"/>; the facade owns the delivery-log persistence.
/// </summary>
public interface IEmailSender
{
    /// <summary>Hand a rendered email to the provider. Returns the provider receipt id or an error.</summary>
    Task<SendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>
/// M1: concrete SMS transport (Twilio/Thai SMS gateway). Stateless w.r.t. the DB — see <see cref="IEmailSender"/>.
/// </summary>
public interface ISmsSender
{
    /// <summary>Hand a rendered SMS to the provider. Returns the provider receipt id or an error.</summary>
    Task<SendResult> SendSmsAsync(string toPhone, string message, CancellationToken ct = default);
}
