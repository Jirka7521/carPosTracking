using System.Net;
using CarPosAPI.Middleware;
using Microsoft.AspNetCore.Http;

namespace CarPosAPI.Services.Health;

/// <summary>
/// Decides who gets the full <c>/health</c> report rather than the bare status.
///
/// <para>
/// The report names the runtime and build, the process uptime, the ingest's
/// message counters and the ids of pending migrations — useful to an operator,
/// reconnaissance to anyone else, and the tunnel publishes the endpoint as
/// <c>jimajer.cz/carPosAPI/health</c>. So the detail goes only to a caller inside
/// the container: loopback, or the container's own address (the deployment's
/// healthcheck probes <c>BE_URL</c>, which the network alias resolves straight back
/// to this container).
/// </para>
///
/// <para>
/// Judged on the raw connection, never on a forwarded address: a request that came
/// through a proxy is not internal whatever the proxy says, and nothing from outside
/// can open a connection from this container's own address.
/// </para>
/// </summary>
internal static class HealthDetailPolicy
{
    /// <summary>Whether this request may read the detailed report.</summary>
    /// <param name="context">The probe's request.</param>
    /// <returns>True for a caller inside the container.</returns>
    public static bool MayReadDetails(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ForwardedClient? forwarded = context.Features.Get<ForwardedClient>();

        return IsInternal(
            forwarded is null ? context.Connection.RemoteIpAddress : forwarded.PeerAddress,
            context.Connection.LocalIpAddress,
            forwarded?.ViaTrustedProxy ?? false);
    }

    /// <summary>The rule itself, over plain values so it can be tested directly.</summary>
    /// <param name="peerAddress">The address that opened the connection.</param>
    /// <param name="localAddress">The address the connection arrived on.</param>
    /// <param name="viaTrustedProxy">Whether the peer is a configured reverse proxy.</param>
    /// <returns>True when the peer is loopback or this container itself, and not a proxy.</returns>
    public static bool IsInternal(IPAddress? peerAddress, IPAddress? localAddress, bool viaTrustedProxy)
    {
        IPAddress? peer = ForwardedClientResolver.Normalise(peerAddress);

        if (viaTrustedProxy || peer is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(peer))
        {
            return true;
        }

        IPAddress? local = ForwardedClientResolver.Normalise(localAddress);

        return local is not null && !local.Equals(IPAddress.Any) && !local.Equals(IPAddress.IPv6Any) && peer.Equals(local);
    }
}
