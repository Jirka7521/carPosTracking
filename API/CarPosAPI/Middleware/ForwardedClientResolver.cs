using System.Net;
using Microsoft.Extensions.Primitives;

namespace CarPosAPI.Middleware;

/// <summary>
/// Decides which address a request is attributed to. Pure — the connection's peer
/// address and the request headers in, a <see cref="ForwardedClient"/> out — so the
/// whole policy is pinned by unit tests rather than by a deployment.
///
/// <para>
/// It replaces the framework's <c>UseForwardedHeaders</c>, which was configured to
/// believe <c>X-Forwarded-For</c> from <em>any</em> peer and could not read
/// Cloudflare's header at all. The rules, in order:
/// </para>
/// <list type="number">
/// <item>A peer that is not a configured proxy is the client. Its forwarding
/// headers are ignored outright — on the VLAN anyone can send them.</item>
/// <item>From a Cloudflare tunnel connector, <c>CF-Connecting-IP</c> is the client.
/// Cloudflare sets it on every request and a visitor cannot override it.</item>
/// <item>Otherwise, from a trusted proxy, the right-most <c>X-Forwarded-For</c>
/// entry — the one the proxy itself appended. Everything to its left was written by
/// whoever the proxy was talking to, i.e. possibly by the attacker.</item>
/// <item>A trusted proxy that sent neither header is itself the client.</item>
/// </list>
/// <para>
/// <c>X-Forwarded-Proto</c> follows the same trust: believed from a proxy, ignored
/// from anyone else, and only ever <c>http</c> or <c>https</c>.
/// </para>
/// </summary>
internal static class ForwardedClientResolver
{
    /// <summary>Cloudflare's client-address header.</summary>
    public const string CloudflareConnectingIpHeader = "CF-Connecting-IP";

    /// <summary>The de-facto standard client-address header.</summary>
    public const string ForwardedForHeader = "X-Forwarded-For";

    /// <summary>The de-facto standard original-scheme header.</summary>
    public const string ForwardedProtoHeader = "X-Forwarded-Proto";

    /// <summary>Resolves the client behind one request.</summary>
    /// <param name="peerAddress">The connection's remote address, as Kestrel saw it.</param>
    /// <param name="scheme">The connection's own scheme (<c>http</c> inside the container).</param>
    /// <param name="headers">The request headers.</param>
    /// <param name="trustedProxies">Peers whose <c>X-Forwarded-*</c> headers are believed.</param>
    /// <param name="cloudflareProxies">Peers whose <c>CF-Connecting-IP</c> is believed (and preferred).</param>
    /// <returns>The attribution to use for rate limiting, logging and the scheme.</returns>
    public static ForwardedClient Resolve(
        IPAddress? peerAddress,
        string scheme,
        IHeaderDictionary headers,
        IReadOnlySet<IPAddress> trustedProxies,
        IReadOnlySet<IPAddress> cloudflareProxies)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(trustedProxies);
        ArgumentNullException.ThrowIfNull(cloudflareProxies);

        IPAddress? peer = Normalise(peerAddress);

        bool fromCloudflare = peer is not null && cloudflareProxies.Contains(peer);
        bool fromProxy = fromCloudflare || (peer is not null && trustedProxies.Contains(peer));

        if (!fromProxy)
        {
            return new ForwardedClient(peer, peer, scheme, false);
        }

        IPAddress? client = null;

        if (fromCloudflare)
        {
            client = ParseAddress(LastEntry(headers[CloudflareConnectingIpHeader]));
        }

        client ??= ParseAddress(LastEntry(headers[ForwardedForHeader]));

        string? forwardedScheme = LastEntry(headers[ForwardedProtoHeader]);
        string resolvedScheme = string.Equals(forwardedScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? Uri.UriSchemeHttps
            : string.Equals(forwardedScheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                ? Uri.UriSchemeHttp
                : scheme;

        return new ForwardedClient(peer, client ?? peer, resolvedScheme, true);
    }

    /// <summary>
    /// Folds an IPv4-mapped IPv6 address back to IPv4. Kestrel listens dual-stack, so
    /// an IPv4 peer arrives as <c>::ffff:192.168.124.6</c> and would never equal the
    /// configured <c>192.168.124.6</c> without this.
    /// </summary>
    /// <param name="address">The address as reported, or null.</param>
    /// <returns>The comparable form, or null.</returns>
    public static IPAddress? Normalise(IPAddress? address)
    {
        return address is not null && address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    /// <summary>
    /// The right-most comma-separated entry across every copy of a header — the one
    /// the nearest proxy wrote.
    /// </summary>
    /// <param name="values">The header's values; possibly several lines.</param>
    /// <returns>The trimmed last entry, or null when there is none.</returns>
    private static string? LastEntry(StringValues values)
    {
        for (int line = values.Count - 1; line >= 0; line--)
        {
            string? value = values[line];

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string last = value[(value.LastIndexOf(',') + 1)..].Trim();

            return last.Length == 0 ? null : last;
        }

        return null;
    }

    /// <summary>
    /// Parses one entry as an address, tolerating a port (<c>1.2.3.4:5678</c>,
    /// <c>[2001:db8::1]:443</c>) because some proxies append one.
    /// </summary>
    /// <param name="entry">The entry, or null.</param>
    /// <returns>The address, or null when the entry is not one.</returns>
    private static IPAddress? ParseAddress(string? entry)
    {
        if (entry is null)
        {
            return null;
        }

        return IPEndPoint.TryParse(entry, out IPEndPoint? endpoint) ? Normalise(endpoint.Address) : null;
    }
}
