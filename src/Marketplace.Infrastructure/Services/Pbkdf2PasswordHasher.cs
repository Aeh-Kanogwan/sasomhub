using System.Security.Cryptography;
using Marketplace.Application.Common;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// NFR-S1 password hashing using PBKDF2 (HMAC-SHA256) from the BCL — zero extra NuGet dependency
/// (no Microsoft.Extensions.Identity.Core / KeyDerivation package needed). The stored value is a
/// self-describing string so iteration count / salt size can change later without breaking old hashes:
/// <c>PBKDF2$&lt;iterations&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c> (well under the 512-char PasswordHash column).
///
/// Verify is constant-time (CryptographicOperations.FixedTimeEquals) and never throws on malformed input —
/// a bad/legacy hash simply returns false so login fails closed.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "PBKDF2";
    private const int SaltSize = 16;            // 128-bit salt
    private const int HashSize = 32;            // 256-bit derived key
    private const int Iterations = 100_000;     // OWASP-aligned baseline for PBKDF2-HMAC-SHA256
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);

        return string.Join('$',
            Prefix,
            Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
