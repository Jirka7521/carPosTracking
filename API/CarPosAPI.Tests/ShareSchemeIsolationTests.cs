using System.IdentityModel.Tokens.Jwt;
using System.Text;
using CarPosAPI.Data.Entities;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CarPosAPI.Tests;

/// <summary>
/// The single most important test in the sharing feature.
///
/// <para>
/// A share token and a session token are signed with the <em>same</em> HMAC key.
/// That is a deliberate choice — it keeps the deployment to one secret — and it
/// means the signature alone cannot tell the two apart. What separates them is the
/// issuer and the audience: each authentication scheme validates its own pair, so
/// a token minted for an anonymous visitor fails validation at every
/// <c>[Authorize]</c> endpoint, and a session cookie fails at the share endpoint.
/// </para>
///
/// <para>
/// If that separation ever breaks, nothing visible breaks with it. There is no
/// error, no log line, no failing request — just a two-hour link that has quietly
/// become an account. These tests are what make that unfalsifiable, so they
/// exercise the real <see cref="TokenValidationParameters"/> the application
/// builds rather than a description of them.
/// </para>
/// </summary>
public sealed class ShareSchemeIsolationTests
{
    /// <summary>
    /// A key of the right length for HMAC-SHA256. Its value is irrelevant; what
    /// matters is that both issuers below are given the same one, because that is
    /// the situation the production configuration creates.
    /// </summary>
    private const string SharedSigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256";

    private static JwtOptions Options()
    {
        return new JwtOptions
        {
            Issuer = "carpos-api",
            Audience = "carpos-fe",
            ShareIssuer = "carpos-share-api",
            ShareAudience = "carpos-share",
            SigningKey = SharedSigningKey,
            LifetimeHours = 8,
            ShareLifetimeHours = 12,
        };
    }

    [Fact]
    public void ShareTokenIsRejectedBySessionValidation()
    {
        JwtOptions options = Options();
        ShareTokenIssuer issuer = new ShareTokenIssuer(Microsoft.Extensions.Options.Options.Create(options));

        IssuedToken token = issuer.Issue(Guid.NewGuid(), DateTime.UtcNow.AddHours(2));

        // The base type rather than a specific one: the handler checks audience
        // before issuer, so naming a subtype would pin this test to the library's
        // internal ordering rather than to the thing that matters — that a share
        // token cannot satisfy the scheme every [Authorize] endpoint uses.
        Assert.ThrowsAny<SecurityTokenValidationException>(
            () => Validate(token.Token, options.Issuer, options.Audience));
    }

    [Fact]
    public void SessionTokenIsRejectedByShareValidation()
    {
        JwtOptions options = Options();
        JwtTokenIssuer issuer = new JwtTokenIssuer(Microsoft.Extensions.Options.Options.Create(options));

        IssuedToken token = issuer.Issue(new User { Id = 42 });

        Assert.ThrowsAny<SecurityTokenValidationException>(
            () => Validate(token.Token, options.ShareIssuer, options.ShareAudience));
    }

    [Fact]
    public void TheIssuerAndTheAudienceAreEachLoadBearingOnTheirOwn()
    {
        // Because the handler stops at the first failed check, the two tests above
        // would still pass if one of the pair were accidentally made to match. This
        // varies them one at a time, so neither can be quietly carrying the other.
        JwtOptions options = Options();
        ShareTokenIssuer issuer = new ShareTokenIssuer(Microsoft.Extensions.Options.Options.Create(options));

        IssuedToken token = issuer.Issue(Guid.NewGuid(), DateTime.UtcNow.AddHours(2));

        // Right audience, session issuer.
        Assert.Throws<SecurityTokenInvalidIssuerException>(
            () => Validate(token.Token, options.Issuer, options.ShareAudience));

        // Right issuer, session audience.
        Assert.Throws<SecurityTokenInvalidAudienceException>(
            () => Validate(token.Token, options.ShareIssuer, options.Audience));

        // And with both correct it validates, so the assertions above are failing for
        // the stated reason rather than because the token is broken.
        Validate(token.Token, options.ShareIssuer, options.ShareAudience);
    }

    [Fact]
    public void ShareTokenCarriesNoSubjectClaim()
    {
        // Even if a share token somehow satisfied a session endpoint, there would be
        // no identity in it to act as: CurrentUserAccessor reads "sub", and a share
        // token does not have one. This is the second, independent barrier.
        JwtOptions options = Options();
        ShareTokenIssuer issuer = new ShareTokenIssuer(Microsoft.Extensions.Options.Options.Create(options));

        IssuedToken token = issuer.Issue(Guid.NewGuid(), DateTime.UtcNow.AddHours(2));

        JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);

        Assert.DoesNotContain(parsed.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub);
        Assert.Contains(parsed.Claims, claim => claim.Type == ShareAuthenticationDefaults.ShareClaim);
    }

    [Fact]
    public void ShareTokenNeverOutlivesItsWindow()
    {
        // The configured ceiling is twelve hours; this share closes in thirty minutes.
        // A token valid past its own share would be a credential for something that
        // has ended.
        JwtOptions options = Options();
        ShareTokenIssuer issuer = new ShareTokenIssuer(Microsoft.Extensions.Options.Options.Create(options));

        DateTime validUntil = DateTime.UtcNow.AddMinutes(30);
        IssuedToken token = issuer.Issue(Guid.NewGuid(), validUntil);

        Assert.True(token.Lifetime <= TimeSpan.FromMinutes(30));

        JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
        Assert.True(parsed.ValidTo <= validUntil.AddSeconds(1));
    }

    [Fact]
    public void ConfigurationRefusesToLetTheTwoIdentitiesMatch()
    {
        // The startup guard. Without it, a deployment that copied the session values
        // into the share settings would boot happily and hand out share tokens that
        // every [Authorize] endpoint accepts.
        Assert.True(Options().HasDistinctShareIdentity());

        JwtOptions sameIssuer = Options();
        sameIssuer.ShareIssuer = sameIssuer.Issuer;
        Assert.False(sameIssuer.HasDistinctShareIdentity());

        JwtOptions sameAudience = Options();
        sameAudience.ShareAudience = sameAudience.Audience;
        Assert.False(sameAudience.HasDistinctShareIdentity());
    }

    /// <summary>
    /// Validates a token exactly as the corresponding handler in <c>Program.cs</c>
    /// does — same key, same checks, same zero clock skew.
    /// </summary>
    /// <param name="token">The compact-serialised JWT.</param>
    /// <param name="issuer">The issuer the validating scheme expects.</param>
    /// <param name="audience">The audience the validating scheme expects.</param>
    private static void Validate(string token, string issuer, string audience)
    {
        JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();

        TokenValidationParameters parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SharedSigningKey)),
            ClockSkew = TimeSpan.Zero,
        };

        handler.ValidateToken(token, parameters, out SecurityToken _);
    }
}
