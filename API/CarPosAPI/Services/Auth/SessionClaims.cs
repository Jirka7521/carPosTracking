using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// The session token's own claim names, and the one place that reads them back.
/// Issued by <see cref="JwtTokenIssuer"/>, checked by <see cref="ISessionRegistry"/>.
/// </summary>
public static class SessionClaims
{
    /// <summary>Carries <see cref="Data.Entities.User.SecurityStamp"/>.</summary>
    public const string SecurityStamp = "stamp";

    /// <summary>
    /// Reads the revocation-relevant claims. A token without all four — every token
    /// issued before stamps existed, among others — is not a session this API can
    /// vouch for, and is refused rather than waved through.
    /// </summary>
    /// <param name="principal">The principal the JWT handler built from a validated token.</param>
    /// <param name="identity">The claims, when all are present and well-formed.</param>
    /// <returns>True when the token carries everything the check needs.</returns>
    public static bool TryRead(ClaimsPrincipal? principal, out SessionIdentity identity)
    {
        identity = new SessionIdentity(0, string.Empty, string.Empty, DateTime.MinValue);

        if (principal is null)
        {
            return false;
        }

        string? subject = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        string? stamp = principal.FindFirstValue(SecurityStamp);
        string? tokenId = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        string? expires = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);

        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out int userId)
            || string.IsNullOrEmpty(stamp)
            || string.IsNullOrEmpty(tokenId)
            || !long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out long expiresUnix))
        {
            return false;
        }

        identity = new SessionIdentity(
            userId,
            stamp,
            tokenId,
            DateTimeOffset.FromUnixTimeSeconds(expiresUnix).UtcDateTime);

        return true;
    }
}
