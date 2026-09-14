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
    /// Issuer stamped on <em>share</em> tokens — the credential an anonymous
    /// visitor holds after entering a link's code.
    ///
    /// <para>
    /// <b>Deliberately different from <see cref="Issuer"/>, and the difference is
    /// load-bearing.</b> A share token and a session token are signed with the same
    /// key, so the only thing standing between "anonymous visitor with a two-hour
    /// window" and "signed-in account" is that each is validated against its own
    /// issuer and audience by its own authentication scheme. Point these at the
    /// session values and that wall disappears silently — no error, just a share
    /// token that a <c>[Authorize]</c> endpoint would accept.
    /// <c>ShareSchemeIsolationTests</c> exists to make that unfalsifiable.
    /// </para>
    /// </summary>
    [Required]
    public string ShareIssuer { get; set; } = "carpos-share-api";

    /// <summary>Audience stamped on share tokens. See <see cref="ShareIssuer"/>.</summary>
    [Required]
    public string ShareAudience { get; set; } = "carpos-share";

    /// <summary>
    /// Ceiling on a share session, in hours.
    ///
    /// The real bound is the link's own window, and the issuer takes whichever is
    /// shorter. This cap only matters for a long window — a month-long share should
    /// not hand out a month-long cookie, because a visitor who opened the link once
    /// on a borrowed phone should have to prove the code again eventually. Twelve
    /// hours keeps a normal day's viewing free of re-prompts.
    /// </summary>
    [Range(1, 168)]
    public int ShareLifetimeHours { get; set; } = 12;

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

    /// <summary>
    /// Validates that share tokens are stamped with an identity of their own.
    ///
    /// Both token kinds are signed with the same key, so issuer and audience are
    /// the entire separation between them. If a deployment sets either share value
    /// equal to its session counterpart, a token minted for an anonymous visitor
    /// starts satisfying <c>[Authorize]</c> — silently, with nothing in any log to
    /// say so. The check costs nothing and the failure it prevents is total, so it
    /// aborts startup rather than warning.
    /// </summary>
    /// <returns><c>true</c> when both the share issuer and audience differ from the session ones.</returns>
    public bool HasDistinctShareIdentity()
    {
        return !string.Equals(ShareIssuer, Issuer, StringComparison.Ordinal)
            && !string.Equals(ShareAudience, Audience, StringComparison.Ordinal);
    }
}
