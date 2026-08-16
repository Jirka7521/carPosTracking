using System.ComponentModel.DataAnnotations;
using System.Text;

namespace CarPosAPI.Options;

/// <summary>
/// Signing and validation settings for the session JWT. Bound from the <c>Jwt</c>
/// configuration section and validated at startup, so a deployment with a missing
/// or too-short key refuses to boot instead of issuing tokens anybody could forge.
///
/// The token itself never reaches JavaScript — it is delivered in an httpOnly
/// cookie (see <see cref="AuthCookieOptions"/>) — but it is still a bearer
/// credential, so the usual rules apply: validate issuer, audience, lifetime and
/// signature, and keep the key out of tracked files.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Minimum signing-key length in bytes. HMAC-SHA256 keys shorter than the hash
    /// output (32 bytes) weaken the MAC, and .NET refuses them outright.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>Token issuer, echoed in the <c>iss</c> claim and validated on every request.</summary>
    [Required]
    public string Issuer { get; set; } = "carpos-api";

    /// <summary>Intended audience, echoed in <c>aud</c> and validated on every request.</summary>
    [Required]
    public string Audience { get; set; } = "carpos-fe";

    /// <summary>
    /// HMAC-SHA256 signing key, at least <see cref="MinimumSigningKeyBytes"/> bytes
    /// of UTF-8. Secret — appsettings.Local.json in development, an environment
    /// variable (<c>Jwt__SigningKey</c>) in production. Never logged, never echoed.
    /// </summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// How long an issued session stays valid. Eight hours is a working day: long
    /// enough that the dashboard is not constantly logging people out, short enough
    /// that a leaked cookie expires on its own. There is no refresh token — signing
    /// in again is the renewal path.
    /// </summary>
    [Range(1, 168)]
    public int LifetimeHours { get; set; } = 8;

    /// <summary>
    /// Validates that <see cref="SigningKey"/> is long enough to sign with. Wired
    /// into the options pipeline in <c>Program.cs</c> so a placeholder or truncated
    /// key aborts startup rather than producing forgeable tokens.
    /// </summary>
    /// <returns><c>true</c> when the key is at least the minimum length.</returns>
    public bool HasStrongSigningKey()
    {
        return Encoding.UTF8.GetByteCount(SigningKey) >= MinimumSigningKeyBytes;
    }
}
