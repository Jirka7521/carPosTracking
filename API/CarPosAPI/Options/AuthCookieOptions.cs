using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// How the session is carried between the browser and the API. Bound from the
/// <c>AuthCookie</c> section; the defaults are the production values, so the
/// section normally does not need to appear in appsettings at all.
///
/// Why cookies rather than an <c>Authorization</c> header: the frontend is served
/// from the same origin as the API (nginx proxies <c>/api</c> to it), so the
/// session can live in an <c>HttpOnly</c> cookie that JavaScript cannot read.
/// That removes the single worst consequence of an XSS bug — a stealable,
/// long-lived token — at the cost of needing CSRF protection, which
/// <see cref="Middleware.CsrfProtectionMiddleware"/> provides with the
/// double-submit token named below.
/// </summary>
public sealed class AuthCookieOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "AuthCookie";

    /// <summary>
    /// Name of the httpOnly cookie holding the JWT. The <c>__Host-</c> prefix is
    /// deliberately avoided: it mandates <c>Secure</c> and no <c>Domain</c>, which
    /// breaks plain-HTTP local development for no gain over the explicit flags set
    /// on the cookie anyway.
    /// </summary>
    [Required]
    public string SessionCookieName { get; set; } = "carpos_session";

    /// <summary>
    /// Name of the readable double-submit CSRF cookie. This one is intentionally
    /// <em>not</em> httpOnly — the frontend has to read it to echo the value back
    /// in <see cref="CsrfHeaderName"/>. It is not a credential on its own: it
    /// authorises nothing without the session cookie beside it.
    /// </summary>
    [Required]
    public string CsrfCookieName { get; set; } = "carpos_csrf";

    /// <summary>Header the frontend echoes the CSRF cookie value in on every mutation.</summary>
    [Required]
    public string CsrfHeaderName { get; set; } = "X-CSRF-Token";

    /// <summary>
    /// Whether the cookies carry the <c>Secure</c> flag on <em>every</em> response,
    /// whatever scheme the request came in on.
    ///
    /// <para>
    /// False does not mean "never Secure" — see <see cref="UseSecureFlag"/>: a cookie
    /// issued in answer to an HTTPS request is Secure either way. False only lets a
    /// plain-HTTP request (Vite in development, the dashboard on a LAN address) get a
    /// cookie it can actually send back; true additionally refuses that, for a
    /// deployment that is never meant to be reached without TLS.
    /// </para>
    /// </summary>
    public bool SecureCookies { get; set; } = true;

    /// <summary>
    /// Name of the httpOnly cookie holding an anonymous visitor's share token.
    ///
    /// Separate from <see cref="SessionCookieName"/> so the two credentials cannot
    /// be confused for one another at any layer: different cookie, different
    /// authentication scheme, different issuer and audience. A browser that holds
    /// both — an account holder previewing a link they made — sends both, and each
    /// endpoint reads only the one it named.
    /// </summary>
    [Required]
    public string ShareCookieName { get; set; } = "carpos_share";

    /// <summary>
    /// Decides the <c>Secure</c> flag for a cookie written in answer to one request:
    /// always when <see cref="SecureCookies"/> is on, and otherwise whenever the
    /// request itself arrived over HTTPS.
    ///
    /// <para>
    /// The second half is what lets one deployment serve the public site through the
    /// TLS tunnel and the LAN copy over plain HTTP without giving up the flag on the
    /// public side. It used to be all or nothing: turning the switch off for the LAN
    /// also sent the public site's session cookie without <c>Secure</c>, so any
    /// <c>http://</c> request to the domain could carry it in the clear.
    /// </para>
    ///
    /// <para>
    /// The scheme is trustworthy here because <see cref="Middleware.ForwardedClientMiddleware"/>
    /// has already settled it: <c>X-Forwarded-Proto</c> is believed only from the
    /// configured proxies, so a client cannot talk its way into — or out of — a
    /// Secure cookie by sending the header itself.
    /// </para>
    ///
    /// Issue and delete must call this alike: a deletion whose flags differ from the
    /// cookie's is ignored by the browser, and "sign out" then silently does nothing.
    /// </summary>
    /// <param name="request">The request the cookie is being written in answer to.</param>
    /// <returns>True when the cookie must carry <c>Secure</c>.</returns>
    public bool UseSecureFlag(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SecureCookies || request.IsHttps;
    }
}
