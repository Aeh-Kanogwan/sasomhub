using Microsoft.Extensions.Configuration;

namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// M1 provider selection &amp; transport settings, bound from configuration section "Notifications".
/// Default mode is <see cref="EmailProvider.Log"/> / <see cref="SmsProvider.Log"/> so the solution
/// runs out-of-the-box with NO real credentials (dev fallback writes to the log + persists the
/// delivery row). Production overrides EmailProvider/SmsProvider + supplies host/key/pepper via the
/// host's secret store (env vars / user-secrets / KeyVault), never committed to appsettings.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public EmailProvider EmailProvider { get; set; } = EmailProvider.Log;
    public SmsProvider SmsProvider { get; set; } = SmsProvider.Log;

    /// <summary>"From" identity for outbound email (used by SMTP + SendGrid).</summary>
    public string FromEmail { get; set; } = "no-reply@neonvault.local";
    public string FromName { get; set; } = "Sasom Hub";

    /// <summary>
    /// HMAC-SHA256 pepper for <c>RecipientHash</c> (PDPA: lets us match/dedupe a recipient without
    /// storing plaintext PII). DEV default below is fine for local runs; PRODUCTION MUST override this
    /// from a secret store (env <c>Notifications__RecipientHashPepper</c> / user-secrets / KeyVault).
    /// </summary>
    public string RecipientHashPepper { get; set; } = "dev-only-notification-pepper-change-me";

    /// <summary>PDPA retention window for delivery-log evidence (LEGAL #8). Default 5 years.</summary>
    public int RetentionYears { get; set; } = 5;

    public SmtpOptions Smtp { get; set; } = new();
    public SendGridOptions SendGrid { get; set; } = new();
    public SmsGatewayOptions Sms { get; set; } = new();

    /// <summary>
    /// Reads the "Notifications" section via the IConfiguration indexer (keeps Infrastructure free of
    /// the Microsoft.Extensions.Configuration.Binder package). Missing keys keep the C# defaults above.
    /// </summary>
    public static NotificationOptions FromConfiguration(IConfigurationSection section)
    {
        var o = new NotificationOptions();
        if (section is null || !section.Exists()) return o;

        if (Enum.TryParse<EmailProvider>(section["EmailProvider"], ignoreCase: true, out var ep)) o.EmailProvider = ep;
        if (Enum.TryParse<SmsProvider>(section["SmsProvider"], ignoreCase: true, out var sp)) o.SmsProvider = sp;
        o.FromEmail = Or(section["FromEmail"], o.FromEmail);
        o.FromName = Or(section["FromName"], o.FromName);
        o.RecipientHashPepper = Or(section["RecipientHashPepper"], o.RecipientHashPepper);
        if (int.TryParse(section["RetentionYears"], out var ry)) o.RetentionYears = ry;

        var smtp = section.GetSection("Smtp");
        o.Smtp.Host = Or(smtp["Host"], o.Smtp.Host);
        if (int.TryParse(smtp["Port"], out var port)) o.Smtp.Port = port;
        if (bool.TryParse(smtp["EnableSsl"], out var ssl)) o.Smtp.EnableSsl = ssl;
        o.Smtp.Username = NullIfBlank(smtp["Username"]);
        o.Smtp.Password = NullIfBlank(smtp["Password"]);
        if (int.TryParse(smtp["TimeoutMs"], out var stm)) o.Smtp.TimeoutMs = stm;

        var sg = section.GetSection("SendGrid");
        o.SendGrid.ApiBaseUrl = Or(sg["ApiBaseUrl"], o.SendGrid.ApiBaseUrl);
        o.SendGrid.ApiKey = NullIfBlank(sg["ApiKey"]);

        var sms = section.GetSection("Sms");
        o.Sms.Url = NullIfBlank(sms["Url"]);
        o.Sms.ApiKey = NullIfBlank(sms["ApiKey"]);
        o.Sms.ApiKeyHeader = Or(sms["ApiKeyHeader"], o.Sms.ApiKeyHeader);
        if (int.TryParse(sms["TimeoutMs"], out var gtm)) o.Sms.TimeoutMs = gtm;

        return o;
    }

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public enum EmailProvider { Log, Smtp, SendGrid }
public enum SmsProvider { Log, HttpGateway }

/// <summary>SMTP transport via BCL System.Net.Mail.SmtpClient (no extra NuGet).</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    /// <summary>Leave blank for anonymous relays; PRODUCTION creds come from the secret store.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int TimeoutMs { get; set; } = 15000;
}

/// <summary>SendGrid via raw HTTPS (HttpClient) — no SendGrid SDK NuGet.</summary>
public sealed class SendGridOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.sendgrid.com/v3/mail/send";
    /// <summary>PRODUCTION secret — supply via secret store, never commit.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>Generic HTTP SMS gateway: POST {to, message} with an API key header.</summary>
public sealed class SmsGatewayOptions
{
    public string? Url { get; set; }
    /// <summary>PRODUCTION secret — supply via secret store, never commit.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Header name the gateway expects the API key in (default Authorization Bearer-style).</summary>
    public string ApiKeyHeader { get; set; } = "Authorization";
    public int TimeoutMs { get; set; } = 15000;
}
