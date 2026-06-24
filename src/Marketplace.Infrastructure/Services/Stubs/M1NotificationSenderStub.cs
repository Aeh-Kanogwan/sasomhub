using Marketplace.Application.Notifications;
using Marketplace.Application.Common;

namespace Marketplace.Infrastructure.Services.Stubs;

// PHASE A PLACEHOLDER (M1). Lets the solution build & boot before backend-dev implements the real
// Email/SMS providers + delivery-log persistence. backend-dev(M1) replaces these by creating real
// classes (e.g. SendGridEmailSender, TwilioSmsSender, NotificationSender) in their own files and
// swapping the registration inside ModuleRegistration.AddM1Notifications (see ModuleRegistration.cs).
// DO NOT add business logic here.

internal sealed class M1NotificationSenderStub : INotificationSender
{
    public Task<Result<SendResult>> SendAsync(SendMessageRequest request, CancellationToken ct = default)
        => throw new NotImplementedException("M1: INotificationSender not yet implemented (Phase A stub).");
}

internal sealed class M1EmailSenderStub : IEmailSender
{
    public Task<SendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        => throw new NotImplementedException("M1: IEmailSender not yet implemented (Phase A stub).");
}

internal sealed class M1SmsSenderStub : ISmsSender
{
    public Task<SendResult> SendSmsAsync(string toPhone, string message, CancellationToken ct = default)
        => throw new NotImplementedException("M1: ISmsSender not yet implemented (Phase A stub).");
}
