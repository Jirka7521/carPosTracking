using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CarPosAPI.Data.Entities;
using CarPosAPI.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Signs session JWTs with the configured HMAC key.
///
/// The token deliberately carries the bare minimum: the user id and the standard
/// registered claims. No email, no name, and above all no permission flags —
/// authorisation is re-read from the database on every request, so baking it into
/// a token that lives for hours would only mean a revoked share keeps working
/// until it expires.
///
/// Singleton: it holds immutable options and a reusable signing credential.
/// </summary>
internal sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JwtSecurityTokenHandler _handler = new JwtSecurityTokenHandler();

    /// <summary>Creates the issuer and derives its signing credentials once.</summary>
    /// <param name="options">Validated JWT settings (key length is checked at startup).</param>
    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public IssuedToken Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        TimeSpan lifetime = TimeSpan.FromHours(_options.LifetimeHours);

        DateTime issuedAtUtc = DateTime.UtcNow;
        DateTime expiresAtUtc = issuedAtUtc.Add(lifetime);

        // 'sub' is the user id and 'jti' a unique token id — the latter is not used
        // for revocation today (there is no deny-list), but it makes two tokens
        // issued in the same second distinguishable in a log or a support ticket.
        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        ];

        JwtSecurityToken token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: expiresAtUtc,
            signingCredentials: _credentials);

        // The lifetime, not the absolute expiry: the cookie that carries this token
        // is given a Max-Age, which the browser resolves against its own clock. See
        // IssuedToken.
        return new IssuedToken(_handler.WriteToken(token), lifetime);
    }
}
