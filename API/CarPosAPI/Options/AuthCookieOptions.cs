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
    /// Whether the cookies carry the <c>Secure</c> flag. True everywhere real; set
    /// to false only for plain-HTTP local development, where a Secure cookie would
    /// simply never be sent back. Production must leave this at the default —
    /// without it the session travels in the clear on any accidental HTTP request.
    /// </summary>
    public bool SecureCookies { get; set; } = true;
}
