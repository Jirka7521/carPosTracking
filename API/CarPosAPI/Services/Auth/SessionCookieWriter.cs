using System.Security.Cryptography;
using CarPosAPI.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Writes the pair of cookies a session consists of:
///
/// <list type="bullet">
/// <item><description>
/// the <b>session cookie</b> holding the JWT — <c>HttpOnly</c>, so no script can
/// read it, which is what makes an XSS bug survivable rather than fatal;
/// </description></item>
/// <item><description>
/// the <b>CSRF cookie</b> holding a random token — readable by design, because the
/// frontend must echo it back in a header. It is not a credential: it grants
/// nothing on its own, and its whole job is to prove the request was written by
/// code that could <em>read</em> our origin's cookies, which a cross-site attacker
/// cannot.
/// </description></item>
/// </list>
///
/// Both are <c>SameSite=Strict</c>, which alone stops the common CSRF cases; the
/// double-submit token is the belt to that suspenders, covering the corners
/// (older browsers, and same-site-but-untrusted subdomains) where SameSite is not
/// enough. Both also expire by <c>Max-Age</c> rather than <c>Expires</c>, so the
/// session does not depend on this server's clock agreeing with the browser's —
/// see <see cref="Issue"/>. Singleton — it holds only immutable options.
/// </summary>
internal sealed class SessionCookieWriter : ISessionCookieWriter
{
    /// <summary>Bytes of entropy in the CSRF token. 32 is overkill on purpose; it is free.</summary>
    private const int CsrfTokenBytes = 32;

    private readonly AuthCookieOptions _options;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">Cookie names and the Secure-flag switch.</param>
    public SessionCookieWriter(IOptions<AuthCookieOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Writes both cookies with a <c>Max-Age</c> equal to the token's lifetime.
    ///
    /// <para>
    /// <c>Max-Age</c> rather than <c>Expires</c>, and that is the whole point of
    /// this method. <c>Expires</c> is an absolute moment written by us and judged
    /// by the browser against <em>its</em> clock, so the session only survives if
    /// the two machines agree about the time. When this server's clock ran days
    /// slow, every sign-in shipped a cookie whose expiry was already in the past:
    /// the browser discarded it on arrival, the login response looked like a
    /// success, and the very next request came back 401 — which the frontend reads
    /// as an expired session and answers by bouncing back to the login page. A
    /// login form that silently reloads itself is a long way from "check the clock
    /// on the server", so the cookie no longer depends on the answer.
    /// </para>
    ///
    /// <para>
    /// <c>Max-Age</c> is a duration the browser resolves against the same clock it
    /// will later use to decide the cookie has expired, so any drift cancels out.
    /// The JWT inside is unaffected either way: it is minted and validated against
    /// this server's clock alone, so it stays self-consistent however wrong that
    /// clock is. Note that drift large enough to matter here breaks other things
    /// too — position ingest rejects fixes outside its clock-skew window — so this
    /// makes sign-in survive a wrong clock, it does not make one harmless.
    /// </para>
    /// </summary>
    /// <param name="response">The response being sent to the browser.</param>
    /// <param name="token">The freshly issued session token and its lifetime.</param>
    public void Issue(HttpResponse response, IssuedToken token)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(token);

        response.Cookies.Append(_options.SessionCookieName, token.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = _options.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            // Matching the token's own lifetime keeps the two from drifting apart: a
            // cookie that outlives its token produces requests the browser believes
            // are authenticated and the server answers 401.
            MaxAge = token.Lifetime,
        });

        response.Cookies.Append(_options.CsrfCookieName, GenerateCsrfToken(), new CookieOptions
        {
            // Readable on purpose — see the class summary.
            HttpOnly = false,
            Secure = _options.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = token.Lifetime,
        });
    }

    /// <inheritdoc />
    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // The delete options must match the ones the cookie was set with (path,
        // Secure, SameSite) or the browser keeps the original around and "log out"
        // silently does nothing.
        //
        // Deletion is the one place an absolute expiry is still safe, so the
        // asymmetry with Issue() above is deliberate rather than an oversight:
        // Cookies.Delete stamps the epoch, and 1970 is in the past no matter how
        // far either clock has wandered.
        CookieOptions deletion = new CookieOptions
        {
            Secure = _options.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        };

        response.Cookies.Delete(_options.SessionCookieName, deletion);
        response.Cookies.Delete(_options.CsrfCookieName, deletion);
    }

    /// <summary>Generates a fresh CSRF token.</summary>
    /// <returns>URL-safe base64 of <see cref="CsrfTokenBytes"/> cryptographically random bytes.</returns>
    private static string GenerateCsrfToken()
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(CsrfTokenBytes);

        // Base64Url rather than plain base64: '+', '/' and '=' would be
        // percent-encoded on the way into the cookie, and the frontend would then
        // have to remember to decode before echoing the value back — a mismatch
        // that shows up as every mutation failing CSRF validation.
        return WebEncoders.Base64UrlEncode(tokenBytes);
    }
}
