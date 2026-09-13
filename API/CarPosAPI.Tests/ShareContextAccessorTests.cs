using System.Security.Claims;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CarPosAPI.Tests;

/// <summary>
/// The join between the share scheme and the code that reads it.
///
/// <para>
/// <see cref="ShareContextAccessor"/> finds the share identity by its
/// <see cref="ClaimsIdentity.AuthenticationType"/>, which is how a visitor who is
/// also signed in cannot have one session answer for the other. Nothing in the
/// token library makes that name the scheme name on its own — its default is
/// <c>"AuthenticationTypes.Federation"</c> for every scheme alike — so the share
/// handler has to ask for it, and this is where that is checked.
/// </para>
///
/// <para>
/// The failure it guards against is a quiet one. Authorization still succeeds,
/// because the token really is valid; the request then dies inside the action with
/// "a share endpoint was reached without a share claim", which reads like a
/// misconfigured <c>[Authorize]</c> rather than a naming mismatch three files away.
/// </para>
/// </summary>
public sealed class ShareContextAccessorTests
{
    private static JwtOptions Options()
    {
        return new JwtOptions
        {
            Issuer = "carpos-api",
            Audience = "carpos-fe",
            ShareIssuer = "carpos-share-api",
            ShareAudience = "carpos-share",
            SigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256",
            LifetimeHours = 8,
            ShareLifetimeHours = 12,
        };
    }

    [Fact]
    public async Task AShareTokenValidatedByTheRealParametersIsReadableAsync()
    {
        // End to end across the seam: issue as the redemption endpoint does, validate
        // with the parameters Program.cs hands the handler, and read the id back the
        // way the view action does.
        JwtOptions options = Options();
        ShareTokenIssuer issuer = new ShareTokenIssuer(Microsoft.Extensions.Options.Options.Create(options));

        Guid shareId = Guid.NewGuid();
        IssuedToken token = issuer.Issue(shareId, DateTime.UtcNow.AddHours(2));

        ClaimsIdentity identity = await ValidateAsync(token.Token, ShareTokenValidation.CreateParameters(options));

        Assert.Equal(shareId, Accessor(identity).ShareId);
    }

    [Fact]
    public void TheDefaultIdentityNameWouldNotBeFound()
    {
        // Why the parameters must name the identity: the same claims under the token
        // library's default name are invisible to the accessor. This is the shape of
        // the bug, so that removing AuthenticationType from the parameters fails here
        // with an explanation rather than only in a running browser.
        ClaimsIdentity federation = new ClaimsIdentity(
            [new Claim(ShareAuthenticationDefaults.ShareClaim, Guid.NewGuid().ToString("D"))],
            TokenValidationParameters.DefaultAuthenticationType);

        Assert.Null(Accessor(federation).ShareId);
    }

    [Fact]
    public void ASessionIdentityAlongsideAShareOneIsIgnored()
    {
        // The case the name-matching exists for: an account holder opening a link they
        // created themselves holds both cookies, so the principal carries both
        // identities. The share id must come from the share one.
        Guid shareId = Guid.NewGuid();

        ClaimsIdentity session = new ClaimsIdentity(
            [new Claim(ShareAuthenticationDefaults.ShareClaim, Guid.NewGuid().ToString("D"))],
            "Bearer");

        ClaimsIdentity share = new ClaimsIdentity(
            [new Claim(ShareAuthenticationDefaults.ShareClaim, shareId.ToString("D"))],
            ShareAuthenticationDefaults.Scheme);

        ShareContextAccessor accessor = Accessor(session, share);

        Assert.Equal(shareId, accessor.ShareId);
    }

    [Fact]
    public void NoIdentityAtAllIsNoShare()
    {
        Assert.Null(Accessor(new ClaimsIdentity()).ShareId);
    }

    /// <summary>Validates a token the way the JWT bearer handler does.</summary>
    /// <param name="token">The compact-serialised JWT.</param>
    /// <param name="parameters">The validating scheme's parameters.</param>
    /// <returns>The identity the handler would put on the request.</returns>
    private static async Task<ClaimsIdentity> ValidateAsync(string token, TokenValidationParameters parameters)
    {
        // JsonWebTokenHandler rather than JwtSecurityTokenHandler: it is what
        // JwtBearerHandler uses by default, and it is what decides the identity's
        // name from the parameters.
        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.True(result.IsValid);

        return result.ClaimsIdentity;
    }

    /// <summary>An accessor over a request carrying the given identities.</summary>
    /// <param name="identities">The identities on the request principal.</param>
    /// <returns>The accessor under test.</returns>
    private static ShareContextAccessor Accessor(params ClaimsIdentity[] identities)
    {
        DefaultHttpContext context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identities),
        };

        return new ShareContextAccessor(new HttpContextAccessor { HttpContext = context });
    }
}
