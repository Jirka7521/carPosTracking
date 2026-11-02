using CarPosAPI.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// <see cref="IPasswordHasher"/> on top of ASP.NET Core Identity's
/// <see cref="PasswordHasher{TUser}"/> — PBKDF2-HMAC-SHA512 at
/// <see cref="IterationCount"/> iterations, a random 128-bit salt per password, and
/// a constant-time comparison, all of it maintained by the framework rather than by
/// us.
///
/// The generic parameter is only a marker for Identity's API; no user instance is
/// ever needed, so a single shared throwaway is passed in. That is also why this
/// class is a safe singleton: it holds no per-request state.
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>
    /// PBKDF2 rounds. OWASP's Password Storage Cheat Sheet recommends 210,000 for
    /// PBKDF2-HMAC-SHA512; the framework's own default is still 100,000. The count is
    /// embedded in every stored hash, so raising it here does not lock anyone out:
    /// an older hash still verifies, comes back as
    /// <see cref="PasswordCheckResult.ValidNeedsRehash"/>, and is rewritten at the
    /// owner's next sign-in.
    /// </summary>
    public const int IterationCount = 210_000;

    /// <summary>
    /// Identity's hasher takes a user object it never actually reads for the
    /// PBKDF2 formats (it exists for custom implementations that salt with user
    /// data). One shared instance keeps that quirk in this file only.
    /// </summary>
    private static readonly User UnusedUser = new User();

    private readonly PasswordHasher<User> _hasher = new PasswordHasher<User>(
        new OptionsWrapper<PasswordHasherOptions>(new PasswordHasherOptions
        {
            IterationCount = IterationCount,
        }));

    /// <summary>
    /// A hash of a random string nobody knows, made with the current parameters. It
    /// is what <see cref="SimulateCheck"/> verifies against. Lazy so start-up does
    /// not pay for a quarter-million PBKDF2 rounds before the first request that
    /// needs it.
    /// </summary>
    private readonly Lazy<string> _dummyHash;

    /// <summary>Creates the hasher.</summary>
    public PasswordHasher()
    {
        _dummyHash = new Lazy<string>(
            () => _hasher.HashPassword(UnusedUser, Guid.NewGuid().ToString("N")),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return _hasher.HashPassword(UnusedUser, password);
    }

    /// <inheritdoc />
    public PasswordCheckResult Check(string storedHash, string password)
    {
        ArgumentNullException.ThrowIfNull(storedHash);
        ArgumentNullException.ThrowIfNull(password);

        PasswordVerificationResult result = _hasher.VerifyHashedPassword(UnusedUser, storedHash, password);

        return result switch
        {
            PasswordVerificationResult.Success => PasswordCheckResult.Valid,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheckResult.ValidNeedsRehash,
            _ => PasswordCheckResult.Failed,
        };
    }

    /// <inheritdoc />
    public PasswordCheckResult SimulateCheck(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        // The verdict is thrown away by the caller; the work is the point. Reported
        // honestly anyway, so a caller that did look could not be misled.
        return Check(_dummyHash.Value, password);
    }
}
