using CarPosAPI.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// <see cref="IPasswordHasher"/> on top of ASP.NET Core Identity's
/// <see cref="PasswordHasher{TUser}"/> — PBKDF2-HMAC-SHA512, 100k+ iterations, a
/// random 128-bit salt per password, and a constant-time comparison, all of it
/// maintained by the framework rather than by us.
///
/// The generic parameter is only a marker for Identity's API; no user instance is
/// ever needed, so a single shared throwaway is passed in. That is also why this
/// class is a safe singleton: it holds no per-request state.
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>
    /// Identity's hasher takes a user object it never actually reads for the
    /// PBKDF2 formats (it exists for custom implementations that salt with user
    /// data). One shared instance keeps that quirk in this file only.
    /// </summary>
    private static readonly User UnusedUser = new User();

    private readonly PasswordHasher<User> _hasher = new PasswordHasher<User>();

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
}
