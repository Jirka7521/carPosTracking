using System.Text;
using CarPosAPI.Options;
using Microsoft.IdentityModel.Tokens;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Builds the validation parameters the share authentication scheme runs with.
///
/// <para>
/// It lives here rather than inline in <c>Program.cs</c> for one reason: two of
/// these settings are silently load-bearing, and a test can only hold them to
/// account if it can reach the same object the application uses. A test that
/// re-declared the parameters would keep passing while the running scheme drifted
/// away from it, which is the failure mode the whole share feature is written to
/// avoid.
/// </para>
/// </summary>
internal static class ShareTokenValidation
{
    /// <summary>
    /// The parameters for validating a share token.
    /// </summary>
    /// <param name="options">Validated JWT settings.</param>
    /// <returns>Parameters accepting share tokens and nothing else.</returns>
    public static TokenValidationParameters CreateParameters(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new TokenValidationParameters
        {
            // The issuer and the audience are the entire separation between a share
            // token and a session token, since both are signed with the same key.
            ValidateIssuer = true,
            ValidIssuer = options.ShareIssuer,
            ValidateAudience = true,
            ValidAudience = options.ShareAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),

            // The default five minutes of slack means an "expired" share keeps
            // working for another five. Zero is the honest value.
            ClockSkew = TimeSpan.Zero,

            // Names the identity this scheme builds, and it is load-bearing:
            // ShareContextAccessor picks the share identity out of the principal by
            // this name, so a browser holding both cookies cannot have one session
            // answer for the other. Without it the token library stamps its own
            // default ("AuthenticationTypes.Federation"), the accessor matches
            // nothing, and every share read fails *after* authorization has already
            // passed — a 500, not a 401.
            AuthenticationType = ShareAuthenticationDefaults.Scheme,
        };
    }
}
