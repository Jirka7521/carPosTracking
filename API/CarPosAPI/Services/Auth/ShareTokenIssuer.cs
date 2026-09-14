using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CarPosAPI.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Signs share tokens — the credential an anonymous visitor holds between entering
/// a code and the window closing.
///
/// <para>
/// <b>It is the same signing key as a session token and a deliberately different
/// identity.</b> Sharing the key keeps the deployment to one secret; the
/// separation is carried instead by
/// <see cref="JwtOptions.ShareIssuer"/>/<see cref="JwtOptions.ShareAudience"/>,
/// which the share scheme validates and the session scheme rejects, and vice
/// versa. <see cref="JwtOptions.HasDistinctShareIdentity"/> refuses to let the
/// application start if the two are ever configured alike, and
/// <c>ShareSchemeIsolationTests</c> proves both directions of rejection.
/// </para>
///
/// <para>
/// <b>There is no <c>sub</c> claim.</b> A share token names a link, never a
/// person, so there is no identity in one to act as even if it reached a code path
/// that wanted one.
/// </para>
///
/// Singleton: it holds immutable options and a reusable signing credential.
/// </summary>
internal sealed class ShareTokenIssuer : IShareTokenIssuer
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JwtSecurityTokenHandler _handler = new JwtSecurityTokenHandler();

    /// <summary>Creates the issuer and derives its signing credentials once.</summary>
    /// <param name="options">Validated JWT settings.</param>
    public ShareTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public IssuedToken Issue(Guid shareId, DateTime validUntilUtc)
    {
        DateTime issuedAtUtc = DateTime.UtcNow;

        // Whichever comes first: the configured ceiling, or the share's own end.
        //
        // The second bound is the one that matters. A token that could outlive its
        // window would be a credential for a share that has closed — and although
        // ShareViewService re-reads the row on every request and would refuse it
        // anyway, a token whose expiry contradicts the thing it grants access to is
        // a trap waiting for the day somebody adds a fast path.
        TimeSpan configured = TimeSpan.FromHours(_options.ShareLifetimeHours);
        TimeSpan remaining = validUntilUtc - issuedAtUtc;
        TimeSpan lifetime = remaining < configured ? remaining : configured;

        // The caller only reaches here having just proved the window is open, so this
        // is a guard against clock movement between that check and this line, not an
        // expected case. A non-positive lifetime would otherwise mint a token that is
        // already expired.
        if (lifetime <= TimeSpan.Zero)
        {
            lifetime = TimeSpan.FromMinutes(1);
        }

        Claim[] claims =
        [
            new Claim(ShareAuthenticationDefaults.ShareClaim, shareId.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        ];

        JwtSecurityToken token = new JwtSecurityToken(
            issuer: _options.ShareIssuer,
            audience: _options.ShareAudience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: issuedAtUtc.Add(lifetime),
            signingCredentials: _credentials);

        return new IssuedToken(_handler.WriteToken(token), lifetime);
    }
}
