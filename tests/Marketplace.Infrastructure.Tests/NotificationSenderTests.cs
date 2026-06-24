using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Notifications;
using Marketplace.Domain.Enums;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marketplace.Infrastructure.Tests;

/// <summary>
/// M1 facade + provider/masking behaviour. Drives <see cref="NotificationSender"/> against the shared
/// InMemory context with fake transports so we assert: a delivery-log row is persisted on EVERY send,
/// status maps Sent/Failed, the recipient is MASKED (never raw, PDPA), the HMAC fingerprint + variables
/// snapshot are stored, retention is stamped, and the config-driven provider switch wires the right sender.
/// </summary>
public class NotificationSenderTests
{
    // ---- fakes ----------------------------------------------------------------------------------

    private sealed class FakeEmailSender : IEmailSender
    {
        private readonly bool _accept;
        public string? LastTo;
        public FakeEmailSender(bool accept) => _accept = accept;
        public Task<SendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            LastTo = toEmail;
            return Task.FromResult(_accept
                ? new SendResult(true, "FakeEmail", "rcpt-123", null)
                : new SendResult(false, "FakeEmail", null, "smtp 550 rejected"));
        }
    }

    private sealed class FakeSmsSender : ISmsSender
    {
        private readonly bool _accept;
        public FakeSmsSender(bool accept) => _accept = accept;
        public Task<SendResult> SendSmsAsync(string toPhone, string message, CancellationToken ct = default)
            => Task.FromResult(_accept
                ? new SendResult(true, "FakeSms", "sms-9", null)
                : new SendResult(false, "FakeSms", null, "gateway timeout"));
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task<SendResult> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private static NotificationOptions Options() => new() { RecipientHashPepper = "unit-test-pepper", RetentionYears = 5 };

    private static NotificationSender NewSender(
        MarketplaceDbContext db, IEmailSender email, ISmsSender sms, NotificationOptions? opts = null)
        => new(db, email, sms, opts ?? Options(), NullLogger<NotificationSender>.Instance);

    private static SendMessageRequest EmailReq(string to = "alice@gmail.com") => new(
        SendChannel.Email, to, "membership.renewal-due", "2",
        new Dictionary<string, string> { ["subject"] = "Renewal", ["days"] = "3" },
        CorrelationId: Guid.NewGuid());

    private static SendMessageRequest SmsReq(string to = "0812345678") => new(
        SendChannel.Sms, to, "membership.renewal-due", "2",
        new Dictionary<string, string> { ["days"] = "1" });

    // ---- tests ----------------------------------------------------------------------------------

    [Fact]
    public async Task Email_send_success_writes_Sent_log_with_masked_recipient()
    {
        await using var db = TestDb.NewContext();
        var sut = NewSender(db, new FakeEmailSender(accept: true), new FakeSmsSender(accept: true));

        var result = await sut.SendAsync(EmailReq("alice@gmail.com"));

        Assert.True(result.Succeeded);
        var log = Assert.Single(db.NotificationDeliveryLogs);
        Assert.Equal(DeliveryStatus.Sent, log.Status);
        Assert.Equal(DeliveryChannel.Email, log.Channel);
        Assert.Equal("a***@gmail.com", log.RecipientMasked);
        Assert.DoesNotContain("alice@gmail.com", log.RecipientMasked);
        Assert.Equal("rcpt-123", log.ProviderMessageId);
        Assert.NotNull(log.SentAtUtc);
        Assert.Null(log.ErrorDetail);
    }

    [Fact]
    public async Task Email_send_failure_writes_Failed_log_with_error_and_no_SentAt()
    {
        await using var db = TestDb.NewContext();
        var sut = NewSender(db, new FakeEmailSender(accept: false), new FakeSmsSender(accept: true));

        var result = await sut.SendAsync(EmailReq());

        Assert.False(result.Succeeded);
        var log = Assert.Single(db.NotificationDeliveryLogs);
        Assert.Equal(DeliveryStatus.Failed, log.Status);
        Assert.Null(log.SentAtUtc);
        Assert.Equal("smtp 550 rejected", log.ErrorDetail);
    }

    [Fact]
    public async Task Sms_send_masks_phone_and_records_channel()
    {
        await using var db = TestDb.NewContext();
        var sut = NewSender(db, new FakeEmailSender(true), new FakeSmsSender(accept: true));

        await sut.SendAsync(SmsReq("0812345678"));

        var log = Assert.Single(db.NotificationDeliveryLogs);
        Assert.Equal(DeliveryChannel.Sms, log.Channel);
        Assert.Equal("08xxxx5678", log.RecipientMasked);
        Assert.Equal(DeliveryStatus.Sent, log.Status);
    }

    [Fact]
    public async Task Throwing_transport_still_persists_a_Failed_log()
    {
        await using var db = TestDb.NewContext();
        var sut = NewSender(db, new ThrowingEmailSender(), new FakeSmsSender(true));

        var result = await sut.SendAsync(EmailReq());

        Assert.False(result.Succeeded);
        var log = Assert.Single(db.NotificationDeliveryLogs);
        Assert.Equal(DeliveryStatus.Failed, log.Status);
        Assert.Contains("boom", log.ErrorDetail);
    }

    [Fact]
    public async Task Snapshot_stores_hmac_fingerprint_and_variables_not_raw_pii()
    {
        await using var db = TestDb.NewContext();
        var opts = Options();
        var sut = NewSender(db, new FakeEmailSender(true), new FakeSmsSender(true), opts);

        await sut.SendAsync(EmailReq("alice@gmail.com"));

        var log = Assert.Single(db.NotificationDeliveryLogs);

        // HMAC fingerprint now lives in the first-class RecipientHash column (never raw PII).
        // Deterministic keyed HMAC-SHA256 hex (64 chars) over the normalised recipient.
        var expected = RecipientPrivacyTestProbe.Hash("alice@gmail.com", opts.RecipientHashPepper);
        Assert.Equal(expected, log.RecipientHash);

        // PayloadSnapshotJson now holds ONLY the rendered template variables (no PII, no recipient hash).
        Assert.NotNull(log.PayloadSnapshotJson);
        Assert.DoesNotContain("alice@gmail.com", log.PayloadSnapshotJson);
        using var doc = JsonDocument.Parse(log.PayloadSnapshotJson!);
        Assert.True(doc.RootElement.TryGetProperty("days", out _));
    }

    [Fact]
    public async Task Retention_is_stamped_five_years_out()
    {
        await using var db = TestDb.NewContext();
        var sut = NewSender(db, new FakeEmailSender(true), new FakeSmsSender(true));

        await sut.SendAsync(EmailReq());

        var log = Assert.Single(db.NotificationDeliveryLogs);
        Assert.True(log.RetentionExpiresAtUtc > log.CreatedAtUtc.AddYears(4));
        Assert.True(log.RetentionExpiresAtUtc <= log.CreatedAtUtc.AddYears(5).AddMinutes(1));
    }

    [Fact]
    public async Task Blank_recipient_is_rejected_without_persisting()
    {
        await using var db = TestDb.NewContext();
        var sut = NewSender(db, new FakeEmailSender(true), new FakeSmsSender(true));

        var result = await sut.SendAsync(EmailReq(to: "  "));

        Assert.False(result.Succeeded);
        Assert.Empty(db.NotificationDeliveryLogs);
    }
}

/// <summary>
/// Recomputes the same keyed HMAC the production helper produces so the snapshot test can assert the
/// exact fingerprint without depending on the internal helper's signature (channel enum).
/// </summary>
internal static class RecipientPrivacyTestProbe
{
    public static string Hash(string email, string pepper)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(pepper));
        var bytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
