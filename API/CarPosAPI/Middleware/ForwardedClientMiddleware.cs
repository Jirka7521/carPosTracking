using System.Net;
using CarPosAPI.Options;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Middleware;

/// <summary>
/// Applies <see cref="ForwardedClientResolver"/> to each request: the resolved
/// client becomes <see cref="ConnectionInfo.RemoteIpAddress"/> and the resolved
/// scheme <see cref="HttpRequest.Scheme"/>, so the rate limiter's per-address
/// partitions, cookie and HSTS decisions and anything logged downstream all see
/// the real client rather than the proxy. The full decision is also stored as a
/// <see cref="ForwardedClient"/> feature, for the one consumer — <c>/health</c> —
/// that has to know about the raw connection.
///
/// <para>
/// Runs right after <c>UsePathBase</c>, where <c>UseForwardedHeaders</c> used to.
/// Anything above it sees the proxy's address.
/// </para>
/// </summary>
internal sealed class ForwardedClientMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlySet<IPAddress> _trustedProxies;
    private readonly IReadOnlySet<IPAddress> _cloudflareProxies;

    /// <summary>Creates the middleware, parsing the proxy lists once.</summary>
    /// <param name="next">The next component in the pipeline.</param>
    /// <param name="options">The configured proxy addresses (validated at start-up).</param>
    public ForwardedClientMiddleware(RequestDelegate next, IOptions<ReverseProxyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _next = next;
        _trustedProxies = options.Value.ParseTrustedProxies();
        _cloudflareProxies = options.Value.ParseCloudflareTunnelProxies();
    }

    /// <summary>Resolves the client and passes the request on.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ForwardedClient client = ForwardedClientResolver.Resolve(
            context.Connection.RemoteIpAddress,
            context.Request.Scheme,
            context.Request.Headers,
            _trustedProxies,
            _cloudflareProxies);

        context.Features.Set(client);
        context.Connection.RemoteIpAddress = client.ClientAddress;
        context.Request.Scheme = client.Scheme;

        return _next(context);
    }
}
