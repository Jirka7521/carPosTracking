using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the attributes the session cookies are written with.
///
/// Chiefly the expiry, which is asserted rather than assumed because getting it
/// wrong is invisible from the server side: an absolute <c>Expires</c> is written
/// here and judged by the browser against its own clock, so a server running more
/// than <c>Jwt:LifetimeHours</c> behind hands out cookies that are already expired
/// when they arrive. The API sees a perfectly successful sign-in; the user sees a
/// login page that reloads itself forever. Nothing short of reading the raw
/// <c>Set-Cookie</c> header catches that, so that is what these do.
/// </summary>
public sealed class SessionCookieWriterTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    /// <summary>Builds a writer over the default cookie names.</summary>
    /// <param name="secureCookies">Whether the cookies should carry <c>Secure</c>.</param>
    /// <returns>The writer under test.</returns>
    private static SessionCookieWriter CreateWriter(bool secureCookies = true)
    {
        AuthCookieOptions options = new AuthCookieOptions { SecureCookies = secureCookies };

        // Fully qualified: the CarPosAPI.Options namespace shadows the static
        // Microsoft.Extensions.Options.Options class this needs.
        return new SessionCookieWriter(Microsoft.Extensions.Options.Options.Create(options));
    }

    /// <summary>Issues a session onto a fresh response and returns its Set-Cookie headers.</summary>
    /// <param name="secureCookies">Whether the cookies should carry <c>Secure</c>.</param>
    /// <returns>One entry per cookie written.</returns>
    private static IReadOnlyList<string> IssueAndReadCookies(bool secureCookies = true)
    {
        DefaultHttpContext context = new DefaultHttpContext();

        CreateWriter(secureCookies).Issue(context.Response, new IssuedToken("a.b.c", Lifetime));

        return context.Response.Headers.SetCookie.ToArray()!;
    }

    [Fact]
    public void ExpiresBothCookiesByMaxAgeRatherThanAnAbsoluteDate()
    {
        IReadOnlyList<string> cookies = IssueAndReadCookies();

        Assert.Equal(2, cookies.Count);

        foreach (string cookie in cookies)
        {
            // The regression this whole file exists for. Max-Age is a duration the
            // browser resolves against its own clock, so drift between the two
            // machines cannot shorten — or entirely erase — the session.
            Assert.Contains($"max-age={(int)Lifetime.TotalSeconds}", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void KeepsTheSessionCookieUnreadableAndTheCsrfCookieReadable()
    {
        IReadOnlyList<string> cookies = IssueAndReadCookies();

        string session = Assert.Single(cookies, (string c) => c.StartsWith("carpos_session=", StringComparison.Ordinal));
        string csrf = Assert.Single(cookies, (string c) => c.StartsWith("carpos_csrf=", StringComparison.Ordinal));

        // The session cookie is the credential, so no script may read it. The CSRF
        // cookie must be readable — the frontend echoes it back in a header.
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", csrf, StringComparison.OrdinalIgnoreCase);

        foreach (string cookie in cookies)
        {
            Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CarriesSecureOnlyWhenConfiguredTo()
    {
        foreach (string cookie in IssueAndReadCookies(secureCookies: true))
        {
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        }

        // False is for plain-HTTP local development only: a Secure cookie is never
        // sent back over http://, which fails in exactly the same silent way the
        // Max-Age test above guards against.
        foreach (string cookie in IssueAndReadCookies(secureCookies: false))
        {
            Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ClearingStampsAnExpiryInThePast()
    {
        DefaultHttpContext context = new DefaultHttpContext();

        CreateWriter().Clear(context.Response);

        string[] cookies = context.Response.Headers.SetCookie.ToArray()!;

        Assert.Equal(2, cookies.Length);

        foreach (string cookie in cookies)
        {
            // Deletion is the one place an absolute date is still safe — the epoch is
            // in the past however far either clock has wandered — so unlike Issue()
            // this one is expected to carry `expires`.
            Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }
}
