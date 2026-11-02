using CarPosAPI.Middleware;
using Microsoft.AspNetCore.Http;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins the baseline headers on API responses — the ones the tunnel's direct
/// <c>/carPosAPI</c> route had none of — and the HSTS rule, which must follow the
/// client's scheme rather than the container's plain-HTTP connection.
/// </summary>
public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public void EveryResponseIsMadeInert()
    {
        DefaultHttpContext context = new DefaultHttpContext();

        SecurityHeadersMiddleware.Apply(context);

        IHeaderDictionary headers = context.Response.Headers;
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Equal("no-referrer", headers["Referrer-Policy"].ToString());
        Assert.Equal("DENY", headers.XFrameOptions.ToString());
        Assert.Contains("frame-ancestors 'none'", headers.ContentSecurityPolicy.ToString());
        Assert.Contains("default-src 'none'", headers.ContentSecurityPolicy.ToString());
    }

    [Fact]
    public void HstsOnlyOverHttps()
    {
        DefaultHttpContext plain = new DefaultHttpContext();
        plain.Request.Scheme = "http";
        SecurityHeadersMiddleware.Apply(plain);
        Assert.False(plain.Response.Headers.ContainsKey("Strict-Transport-Security"));

        DefaultHttpContext secure = new DefaultHttpContext();
        secure.Request.Scheme = "https";
        SecurityHeadersMiddleware.Apply(secure);
        Assert.StartsWith("max-age=", secure.Response.Headers.StrictTransportSecurity.ToString());
    }

    [Fact]
    public void ApplyingTwiceDoesNotDoubleAHeader()
    {
        // The exception handler re-executes the pipeline, so this can run twice for
        // one response.
        DefaultHttpContext context = new DefaultHttpContext();

        SecurityHeadersMiddleware.Apply(context);
        SecurityHeadersMiddleware.Apply(context);

        Assert.Single(context.Response.Headers.XFrameOptions);
    }
}
