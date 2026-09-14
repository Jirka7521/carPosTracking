using CarPosAPI.Options;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Writes the single cookie a share session consists of.
///
/// <para>
/// Unlike <see cref="SessionCookieWriter"/> there is no CSRF companion, because
/// there is nothing for a forged request to accomplish: the only thing this cookie
/// authorises is reading one share, and the endpoint that does so is a
/// <c>GET</c>. The one mutation on the share surface — closing the session — does
/// nothing an attacker wants done.
/// </para>
///
/// <para>
/// <b><c>SameSite=Strict</c>, with a known cost.</b> A visitor arriving by clicking
/// the link in a message makes a cross-site navigation, and a Strict cookie is not
/// sent on one — so returning to the link later means entering the code again
/// rather than resuming. That is the right trade for a page showing where somebody
/// is: the alternative, Lax, would let any site that knows the URL cause the
/// visitor's browser to open an authenticated share. The cookie is set by our own
/// page immediately after the code is accepted, and every request after that is a
/// same-origin fetch, so nothing else about the session depends on this.
/// </para>
///
/// Singleton — it holds only immutable options.
/// </summary>
internal sealed class ShareCookieWriter : IShareCookieWriter
{
    private readonly AuthCookieOptions _options;

    /// <summary>Creates the writer.</summary>
    /// <param name="options">Cookie names and the Secure-flag switch.</param>
    public ShareCookieWriter(IOptions<AuthCookieOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public void Issue(HttpResponse response, IssuedToken token)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(token);

        response.Cookies.Append(_options.ShareCookieName, token.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = _options.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            // Max-Age rather than Expires, for the clock-drift reason set out at
            // length in SessionCookieWriter.Issue. A share window is often only a few
            // hours, which makes a cookie discarded on arrival even harder to tell
            // apart from an expired link.
            MaxAge = token.Lifetime,
        });
    }

    /// <inheritdoc />
    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // The delete options must match the ones the cookie was set with, or the
        // browser keeps the original and "leave" silently does nothing.
        CookieOptions deletion = new CookieOptions
        {
            Secure = _options.SecureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        };

        response.Cookies.Delete(_options.ShareCookieName, deletion);
    }
}
