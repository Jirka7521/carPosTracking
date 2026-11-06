using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using Microsoft.AspNetCore.Http;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the <c>Secure</c> flag on the share visitor's cookie, which follows the
/// same rule as the session cookie (<see cref="AuthCookieOptions.UseSecureFlag"/>):
/// always when forced, and otherwise whenever the request arrived over HTTPS. A
/// share cookie is a credential for somebody's whereabouts, so it must not travel
/// in the clear on the public site just because the LAN copy is plain HTTP.
/// </summary>
public sealed class ShareCookieWriterTests
{
    /// <summary>Builds a writer over the default cookie names.</summary>
    /// <param name="secureCookies">Whether the cookie should always carry <c>Secure</c>.</param>
    /// <returns>The writer under test.</returns>
    private static ShareCookieWriter CreateWriter(bool secureCookies)
    {
        AuthCookieOptions options = new AuthCookieOptions { SecureCookies = secureCookies };

        // Fully qualified: the CarPosAPI.Options namespace shadows the static
        // Microsoft.Extensions.Options.Options class this needs.
        return new ShareCookieWriter(Microsoft.Extensions.Options.Options.Create(options));
    }

    /// <summary>Issues a share session over the given scheme and returns its one cookie.</summary>
    /// <param name="secureCookies">Whether the cookie should always carry <c>Secure</c>.</param>
    /// <param name="scheme">The request's scheme, as ForwardedClientMiddleware settled it.</param>
    /// <returns>The Set-Cookie header written.</returns>
    private static string IssueAndReadCookie(bool secureCookies, string scheme)
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Scheme = scheme;

        CreateWriter(secureCookies).Issue(context.Response, new IssuedToken("a.b.c", TimeSpan.FromHours(1)));

        return Assert.Single(context.Response.Headers.SetCookie.ToArray())!;
    }

    [Fact]
    public void CarriesSecureOverHttpsEvenWhenNotForced()
    {
        Assert.Contains("secure", IssueAndReadCookie(secureCookies: false, scheme: "https"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OmitsSecureOnlyForPlainHttpWhenNotForced()
    {
        Assert.DoesNotContain("secure", IssueAndReadCookie(secureCookies: false, scheme: "http"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", IssueAndReadCookie(secureCookies: true, scheme: "http"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClearingOverHttpsMatchesTheSecureFlagItWasIssuedWith()
    {
        DefaultHttpContext context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        CreateWriter(secureCookies: false).Clear(context.Response);

        string cookie = Assert.Single(context.Response.Headers.SetCookie.ToArray())!;

        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
