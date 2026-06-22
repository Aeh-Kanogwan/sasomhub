using System.Security.Cryptography;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Services;

/// <summary>
/// FR-28 (single-level): generates a short, human-friendly, collision-resistant referral code for a user.
///
/// Design:
///  - Alphabet excludes visually ambiguous characters (0/O, 1/I/L) so codes are easy to read aloud / retype.
///  - Length 8 from a 32-symbol alphabet => 32^8 ≈ 1.1e12 keyspace; collisions are vanishingly rare but still
///    checked against the DB and retried (the column is also protected by UQ_RefCode_Code).
///  - Uses RandomNumberGenerator (CSPRNG) so codes are not guessable/enumerable.
///  - This service ONLY produces a candidate string; it does not persist anything. The caller creates the
///    ReferralCode entity and owns the SaveChanges (so a lost insert race can be retried by the caller too).
/// </summary>
public sealed class ReferralCodeGenerator : IReferralCodeGenerator
{
    // No 0/O, 1/I/L to avoid ambiguity. 32 symbols.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;          // well within VARCHAR(20)
    private const int MaxAttempts = 8;         // guard against pathological collision storms

    private readonly MarketplaceDbContext _db;

    public ReferralCodeGenerator(MarketplaceDbContext db) => _db = db;

    /// <summary>
    /// Returns a code that is not currently present in dbo.ReferralCodes. Retries on the (rare) collision.
    /// The unique index UQ_RefCode_Code remains the source of truth — the caller must still handle a
    /// DbUpdateException on insert (race window between this check and the actual SaveChanges).
    /// </summary>
    public async Task<string> GenerateUniqueAsync(CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var candidate = NewCode();
            var exists = await _db.ReferralCodes.AsNoTracking()
                .AnyAsync(c => c.Code == candidate, ct);
            if (!exists)
                return candidate;
        }

        // Extremely unlikely with this keyspace; fall back to a longer code rather than throwing so that
        // registration is never blocked by code generation (graceful degrade — FR-28).
        return NewCode() + NewCode()[..4];
    }

    /// <summary>Builds one random code from the unambiguous alphabet using a CSPRNG (uniform, no modulo bias).</summary>
    private static string NewCode()
    {
        var chars = new char[CodeLength];
        Span<byte> buffer = stackalloc byte[CodeLength];
        var filled = 0;

        while (filled < CodeLength)
        {
            RandomNumberGenerator.Fill(buffer);
            foreach (var b in buffer)
            {
                // Rejection sampling: drop bytes in the biased tail so every symbol is equiprobable.
                var max = 256 - (256 % Alphabet.Length);
                if (b >= max)
                    continue;
                chars[filled++] = Alphabet[b % Alphabet.Length];
                if (filled == CodeLength)
                    break;
            }
        }

        return new string(chars);
    }
}

/// <summary>
/// FR-28: produces a unique single-level referral code. Defined in Infrastructure (not Application) because
/// uniqueness is enforced against the persistence store; injected as a scoped service alongside the DbContext.
/// </summary>
public interface IReferralCodeGenerator
{
    Task<string> GenerateUniqueAsync(CancellationToken ct = default);
}
