using CarPosAPI.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Middleware;

/// <summary>
/// Double-submit CSRF protection for cookie-authenticated mutations.
///
/// <para>
/// The problem it solves exists only because the session is a cookie: the browser
/// attaches it to <em>every</em> request to this origin, including one triggered
/// by a form on someone else's site. The defence is to demand something a
/// cross-site attacker cannot produce — the value of a cookie belonging to this
/// origin, echoed back in a custom header. Reading that cookie requires
/// same-origin script access; sending a custom header requires a preflight the
/// browser will refuse. Either barrier alone is enough.
/// </para>
///
/// <para>
/// It runs only when a session cookie is actually present. Without one there are
/// no ambient credentials to abuse, and requiring a token would break the very
/// first request anyone makes — signing in.
/// </para>
///
/// <para>
/// This is a second line of defence: the cookies are already
/// <c>SameSite=Strict</c>, which stops the same attacks in any current browser.
/// The pair covers the corners SameSite does not — older clients, and requests
/// from a same-site host that is not us.
/// </para>
/// </summary>
internal sealed class CsrfProtectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthCookieOptions _options;
    private readonly ILogger<CsrfProtectionMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="options">Cookie and header names.</param>
    /// <param name="logger">Structured logger.</param>
    public CsrfProtectionMiddleware(
        RequestDelegate next,
        IOptions<AuthCookieOptions> options,
        ILogger<CsrfProtectionMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Validates the token, or passes the request through.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (RequiresValidation(context.Request))
        {
            string? cookieToken = context.Request.Cookies[_options.CsrfCookieName];
            string? headerToken = context.Request.Headers[_options.CsrfHeaderName];

            bool valid = !string.IsNullOrEmpty(cookieToken)
                && !string.IsNullOrEmpty(headerToken)
                && string.Equals(cookieToken, headerToken, StringComparison.Ordinal);

            if (!valid)
            {
                _logger.LogWarning(
                    "Rejected a {Method} request to {Path}: the CSRF token was missing or did not match",
                    context.Request.Method,
                    context.Request.Path);

                await WriteRejectionAsync(context);
                return;
            }
        }

        await _next(context);
    }

    /// <summary>Decides whether a request has to carry a valid CSRF token.</summary>
    /// <param name="request">The incoming request.</param>
    /// <returns>True when the token must be present and match.</returns>
    private bool RequiresValidation(HttpRequest request)
    {
        // Safe methods change nothing, so there is nothing to forge. (This relies on
        // GET actually being side-effect free — which it is here: every mutation in
        // this API is POST, PUT or DELETE.)
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method)
            || HttpMethods.IsTrace(request.Method))
        {
            return false;
        }

        // No session cookie means no ambient authority to hijack. Requiring a token
        // here would only break sign-in, which is by definition unauthenticated.
        return request.Cookies.ContainsKey(_options.SessionCookieName);
    }

    /// <summary>Writes the 403 that a failed check produces.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the response has been written.</returns>
    private static Task WriteRejectionAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        // ProblemDetails, like every other error this API returns, so the frontend
        // has exactly one error shape to parse.
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Invalid CSRF token",
            Detail = "The request could not be verified. Reload the page and try again.",
        });
    }
}
