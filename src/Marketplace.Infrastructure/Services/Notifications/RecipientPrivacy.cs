using System.Security.Cryptography;
using System.Text;

namespace Marketplace.Infrastructure.Services.Notifications;

/// <summary>
/// PDPA data-minimisation helpers for the delivery log (LEGAL/PDPA). We never store the raw
/// email/phone: the log keeps a human-readable MASKED form (for support context) plus a keyed
/// HMAC-SHA256 fingerprint (for matching/dedupe without re-deriving the PII). The pepper is a
/// secret supplied via configuration; the mask is one-way-ish (drops most characters).
/// </summary>
internal static class RecipientPrivacy
{
    /// <summary>
    /// Mask an email or phone for display in the evidence log.
    /// Email "alice@gmail.com" -> "a***@gmail.com"; phone "0812345678" -> "08xxxx5678".
    /// Unknown/blank input returns a constant so we never accidentally leak the raw value.
    /// </summary>
    public static string Mask(SendChannelKind channel, string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return "(none)";
        var value = recipient.Trim();
        return channel == SendChannelKind.Email ? MaskEmail(value) : MaskPhone(value);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";                      // not a real address shape: reveal nothing
        var local = email[..at];
        var domain = email[(at + 1)..];
        var head = local.Length <= 1 ? local : local[..1];
        return $"{head}***@{domain}";
    }

    private static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "xxxx";           // too short to safely show any tail
        var head = digits.Length >= 6 ? digits[..2] : string.Empty;
        var tail = digits[^4..];
        var middle = new string('x', Math.Max(0, digits.Length - head.Length - tail.Length));
        return head + middle + tail;
    }

    /// <summary>
    /// Keyed HMAC-SHA256 fingerprint of the normalised recipient, hex-encoded. Lets us match the
    /// same recipient across rows without storing PII; the pepper makes it non-reversible by anyone
    /// without the secret. Email is lower-cased; phone is reduced to digits before hashing.
    /// </summary>
    public static string Hash(SendChannelKind channel, string recipient, string pepper)
    {
        var normalised = channel == SendChannelKind.Email
            ? recipient.Trim().ToLowerInvariant()
            : new string(recipient.Where(char.IsDigit).ToArray());

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>Local mirror of the send channel for the privacy helpers (avoids an Application dep here).</summary>
internal enum SendChannelKind { Email, Sms }
