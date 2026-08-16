namespace CarPosAPI.Services.Auth;

/// <summary>
/// Turns passwords into stored hashes and checks them again. A one-method-pair
/// abstraction over the framework's hasher so nothing else in the codebase needs
/// to know which algorithm is in use — or be tempted to reach for a bare
/// <c>SHA256.HashData</c>.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password for storage.</summary>
    /// <param name="password">The plaintext. Never logged, never stored.</param>
    /// <returns>The encoded hash, including its salt and parameters.</returns>
    string Hash(string password);

    /// <summary>Checks a plaintext password against a stored hash.</summary>
    /// <param name="storedHash">The hash from the user row.</param>
    /// <param name="password">The plaintext supplied by the caller.</param>
    /// <returns>Whether it matched, and whether the stored hash should be upgraded.</returns>
    PasswordCheckResult Check(string storedHash, string password);
}
