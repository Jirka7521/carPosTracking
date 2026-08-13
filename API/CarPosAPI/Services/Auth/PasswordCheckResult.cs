namespace CarPosAPI.Services.Auth;

/// <summary>
/// The verdict of checking a password against a stored hash.
///
/// The third value is what makes password storage upgradable: ASP.NET Core's
/// hasher embeds its format version and iteration count in the hash, so when the
/// framework raises those defaults an old-but-correct password comes back as
/// <see cref="ValidNeedsRehash"/> and the login path silently re-hashes it. Without
/// that, existing accounts would keep their day-one iteration count forever.
/// </summary>
public enum PasswordCheckResult
{
    /// <summary>The password does not match. Never say <em>why</em> to the caller.</summary>
    Failed = 0,

    /// <summary>The password matches and the stored hash is current.</summary>
    Valid,

    /// <summary>The password matches but the hash uses outdated parameters.</summary>
    ValidNeedsRehash,
}
