namespace Marketplace.Application.Common;

/// <summary>
/// NFR-S1: one-way password hashing. We persist ONLY the hash (never the plaintext) in
/// <c>User.PasswordHash</c>. The concrete implementation lives in Infrastructure so the password
/// algorithm can evolve without touching callers (AccountController register/login).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Produce a self-describing hash string (algorithm + params + salt + hash) for storage.</summary>
    string Hash(string password);

    /// <summary>Verify a plaintext password against a previously stored hash. Never throws on bad input.</summary>
    bool Verify(string password, string storedHash);
}
