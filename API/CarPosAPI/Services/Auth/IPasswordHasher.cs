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

    /// <summary>
    /// Does the work of <see cref="Check"/> against a hash no password matches. For
    /// the sign-in path when the email has no account: without it that answer came
    /// back a full PBKDF2 run faster than a wrong password, and the difference told
    /// anyone with a stopwatch which addresses are registered here.
    /// </summary>
    /// <param name="password">The plaintext supplied by the caller.</param>
    /// <returns>Always <see cref="PasswordCheckResult.Failed"/> in practice.</returns>
    PasswordCheckResult SimulateCheck(string password);
}
