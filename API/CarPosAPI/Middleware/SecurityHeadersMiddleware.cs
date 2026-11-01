namespace CarPosAPI.Middleware;

/// <summary>
/// Adds the baseline security headers to every API response, the ProblemDetails
/// error bodies included.
///
/// <para>
/// The frontend's nginx already sends a full set on everything it serves, but the
/// tunnel also publishes this API directly as <c>jimajer.cz/carPosAPI</c>, and on
/// that path nothing did. Everything here is JSON, so the policy can be as strict
/// as a policy gets: nothing may be loaded, nothing may frame it, and the browser
/// may not sniff a body into something executable.
/// </para>
///
/// <para>
/// Set from <see cref="HttpResponse.OnStarting(Func{Task})"/> rather than before the
/// next middleware runs, because the exception handler clears the response's
/// headers before writing its 500 — anything set up front would vanish from exactly
/// the responses that most need to be inert.
/// </para>
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    /// <summary>
    /// One year, the same as the frontend's nginx sends for this host; with
    /// <c>includeSubDomains</c> for the same reason it does.
    /// </summary>
    private const string StrictTransportSecurityValue = "max-age=31536000; includeSubDomains";

    /// <summary>
    /// A JSON API loads nothing and is never a page: deny every fetch directive and
    /// every embedding. <c>frame-ancestors</c> is the modern spelling of
    /// <c>X-Frame-Options</c>, which is still sent for older browsers.
    /// </summary>
    private const string ContentSecurityPolicyValue = "default-src 'none'; frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Registers the headers and passes the request on.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(
            static (object state) =>
            {
                Apply((HttpContext)state);
                return Task.CompletedTask;
            },
            context);

        return _next(context);
    }

    /// <summary>
    /// Writes the headers onto a response. Idempotent — assignments, not appends —
    /// so a re-executed pipeline (the exception handler's) cannot double them.
    /// </summary>
    /// <param name="context">The request whose response is about to start.</param>
    public static void Apply(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IHeaderDictionary headers = context.Response.Headers;

        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = ContentSecurityPolicyValue;

        // Only over TLS, as the RFC requires: a browser ignores HSTS received over
        // plain HTTP, and on the LAN's plain-HTTP path it would be a promise this
        // host cannot keep. "Over TLS" means the scheme the resolver settled —
        // https when the tunnel says TLS ended at Cloudflare.
        if (context.Request.IsHttps)
        {
            headers.StrictTransportSecurity = StrictTransportSecurityValue;
        }
    }
}
