using System.Net;

namespace CarPosAPI.Options;

/// <summary>
/// Which peers may tell this API who the real client is. Bound from the
/// <c>ReverseProxy</c> section; nothing here is secret, but getting it wrong is a
/// security bug in both directions, so it is validated at start-up.
///
/// <para>
/// Behind a proxy, every request arrives from the proxy's address. The real
/// client is only known from a header the proxy adds, and a header is just text —
/// anyone who can reach this port directly can send one. The containers sit on a
/// macvlan VLAN, so "anyone" includes every other host on it. The headers are
/// therefore honoured only from the addresses listed here, and ignored from
/// everybody else; that is what keeps the per-address rate limits on sign-in and
/// share redemption from being bypassed with a forged <c>X-Forwarded-For</c>, and
/// what keeps every internet user from landing in the tunnel's one bucket.
/// </para>
///
/// <para>
/// The lists are comma-separated strings rather than arrays on purpose: an
/// environment variable can replace a string outright
/// (<c>ReverseProxy__TrustedProxies=…</c>), whereas overriding an array from the
/// environment replaces it index by index and silently keeps the tail of the
/// default.
/// </para>
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Proxies whose <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are
    /// believed: the frontend's nginx (<c>192.168.124.6</c>, which proxies
    /// <c>/carPosFE/api/*</c>) and the Cloudflare tunnel connector
    /// (<c>192.168.122.50</c>, which routes <c>/carPosAPI/*</c> straight here).
    /// Comma-separated IP addresses; empty trusts nobody.
    /// </summary>
    public string TrustedProxies { get; set; } = "192.168.124.6,192.168.122.50";

    /// <summary>
    /// The Cloudflare tunnel connector(s), whose <c>CF-Connecting-IP</c> is
    /// believed — and preferred over <c>X-Forwarded-For</c>, which Cloudflare appends
    /// to rather than replaces. Cloudflare always sets this header and a client
    /// cannot forge it through the edge. Every address here is trusted for the
    /// other forwarding headers too.
    /// </summary>
    public string CloudflareTunnelProxies { get; set; } = "192.168.122.50";

    /// <summary>The parsed <see cref="TrustedProxies"/>; empty when the value is invalid.</summary>
    /// <returns>Every listed address, IPv4-mapped forms folded to IPv4.</returns>
    public IReadOnlySet<IPAddress> ParseTrustedProxies()
    {
        return TryParseList(TrustedProxies, out IReadOnlySet<IPAddress> parsed) ? parsed : new HashSet<IPAddress>();
    }

    /// <summary>The parsed <see cref="CloudflareTunnelProxies"/>; empty when the value is invalid.</summary>
    /// <returns>Every listed address, IPv4-mapped forms folded to IPv4.</returns>
    public IReadOnlySet<IPAddress> ParseCloudflareTunnelProxies()
    {
        return TryParseList(CloudflareTunnelProxies, out IReadOnlySet<IPAddress> parsed) ? parsed : new HashSet<IPAddress>();
    }

    /// <summary>
    /// Whether both lists parse. Checked at start-up: a typo must not quietly turn
    /// into "trust nobody" (every client then shares the proxy's rate-limit bucket)
    /// any more than into "trust everybody".
    /// </summary>
    /// <returns>True when every entry in both lists is an IP address.</returns>
    public bool HasValidAddresses()
    {
        return TryParseList(TrustedProxies, out _) && TryParseList(CloudflareTunnelProxies, out _);
    }

    /// <summary>Parses one comma-separated list of addresses.</summary>
    /// <param name="value">The configured string; null or blank is an empty list.</param>
    /// <param name="addresses">The addresses, when every entry parsed.</param>
    /// <returns>False when any entry is not an IP address (a CIDR range included).</returns>
    private static bool TryParseList(string? value, out IReadOnlySet<IPAddress> addresses)
    {
        HashSet<IPAddress> parsed = new HashSet<IPAddress>();
        addresses = parsed;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (string entry in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPAddress.TryParse(entry, out IPAddress? address))
            {
                return false;
            }

            parsed.Add(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
        }

        return true;
    }
}
