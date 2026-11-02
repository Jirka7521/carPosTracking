using System.Net;
using CarPosAPI.Middleware;
using CarPosAPI.Options;
using Microsoft.AspNetCore.Http;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins who a request is attributed to. The per-address rate limits on sign-in and
/// share redemption are only as good as this answer, and both ways of getting it
/// wrong are silent: believe every peer and anyone on the VLAN picks their own
/// address with a header; believe no one and every internet visitor shares the
/// tunnel's single bucket. Uses the production defaults, so a change to them is a
/// change these tests notice.
/// </summary>
public sealed class ForwardedClientResolverTests
{
    private static readonly IPAddress Nginx = IPAddress.Parse("192.168.124.6");
    private static readonly IPAddress Cloudflared = IPAddress.Parse("192.168.122.50");
    private static readonly IPAddress LanHost = IPAddress.Parse("192.168.124.77");
    private static readonly IPAddress Visitor = IPAddress.Parse("203.0.113.9");

    private static readonly ReverseProxyOptions Defaults = new ReverseProxyOptions();

    [Fact]
    public void HeadersFromAnUntrustedPeerAreIgnored()
    {
        ForwardedClient client = Resolve(
            LanHost,
            ("X-Forwarded-For", "198.51.100.1"),
            ("CF-Connecting-IP", "198.51.100.2"),
            ("X-Forwarded-Proto", "https"));

        Assert.Equal(LanHost, client.ClientAddress);
        Assert.Equal("http", client.Scheme);
        Assert.False(client.ViaTrustedProxy);
    }

    [Fact]
    public void TheTunnelConnectorIsBelievedThroughCloudflaresHeader()
    {
        // Cloudflare appends to X-Forwarded-For whatever the visitor sent, so the
        // left-hand entry here is attacker-chosen; CF-Connecting-IP is not.
        ForwardedClient client = Resolve(
            Cloudflared,
            ("CF-Connecting-IP", Visitor.ToString()),
            ("X-Forwarded-For", "10.0.0.1, " + Visitor),
            ("X-Forwarded-Proto", "https"));

        Assert.Equal(Visitor, client.ClientAddress);
        Assert.Equal("https", client.Scheme);
        Assert.True(client.ViaTrustedProxy);
    }

    [Fact]
    public void NginxIsBelievedThroughTheRightmostForwardedFor()
    {
        ForwardedClient client = Resolve(
            Nginx,
            ("X-Forwarded-For", "10.9.9.9, " + Visitor));

        Assert.Equal(Visitor, client.ClientAddress);
    }

    [Fact]
    public void CloudflaresHeaderIsNotBelievedFromNginx()
    {
        // nginx is trusted for X-Forwarded-For (which it writes itself) but would pass
        // a CF-Connecting-IP straight through from whoever talked to it — a LAN host,
        // say. Only the connector's copy of that header means anything.
        ForwardedClient client = Resolve(
            Nginx,
            ("CF-Connecting-IP", "198.51.100.2"),
            ("X-Forwarded-For", Visitor.ToString()));

        Assert.Equal(Visitor, client.ClientAddress);
    }

    [Fact]
    public void ATrustedProxyWithoutHeadersIsItselfTheClient()
    {
        ForwardedClient client = Resolve(Nginx);

        Assert.Equal(Nginx, client.ClientAddress);
        Assert.True(client.ViaTrustedProxy);
    }

    [Fact]
    public void AnUnparseableHeaderFallsBackToThePeer()
    {
        ForwardedClient client = Resolve(Cloudflared, ("CF-Connecting-IP", "not-an-address"));

        Assert.Equal(Cloudflared, client.ClientAddress);
    }

    [Fact]
    public void AnIpv4MappedPeerStillMatchesTheConfiguredProxy()
    {
        // Kestrel listens dual-stack, so this is how an IPv4 peer actually arrives.
        ForwardedClient client = Resolve(
            Nginx.MapToIPv6(),
            ("X-Forwarded-For", Visitor.ToString()));

        Assert.Equal(Visitor, client.ClientAddress);
        Assert.Equal(Nginx, client.PeerAddress);
    }

    [Fact]
    public void APortOnAForwardedEntryIsTolerated()
    {
        Assert.Equal(Visitor, Resolve(Nginx, ("X-Forwarded-For", Visitor + ":51234")).ClientAddress);
        Assert.Equal(
            IPAddress.Parse("2001:db8::1"),
            Resolve(Nginx, ("X-Forwarded-For", "[2001:db8::1]:443")).ClientAddress);
    }

    [Fact]
    public void OnlyHttpAndHttpsAreAcceptedAsTheForwardedScheme()
    {
        Assert.Equal("http", Resolve(Cloudflared, ("X-Forwarded-Proto", "javascript")).Scheme);
        Assert.Equal("https", Resolve(Cloudflared, ("X-Forwarded-Proto", "http, HTTPS")).Scheme);
    }

    [Fact]
    public void TheOptionsRejectAnythingButAddresses()
    {
        Assert.True(Defaults.HasValidAddresses());
        Assert.Equal(2, Defaults.ParseTrustedProxies().Count);

        Assert.False(new ReverseProxyOptions { TrustedProxies = "192.168.124.0/24" }.HasValidAddresses());
        Assert.False(new ReverseProxyOptions { CloudflareTunnelProxies = "cloudflared" }.HasValidAddresses());

        // Empty is a legitimate "trust nobody" — a deployment with no proxy at all.
        ReverseProxyOptions none = new ReverseProxyOptions { TrustedProxies = "", CloudflareTunnelProxies = " " };
        Assert.True(none.HasValidAddresses());
        Assert.Empty(none.ParseTrustedProxies());
    }

    /// <summary>Resolves a request from <paramref name="peer"/> against the default proxy lists.</summary>
    /// <param name="peer">The connection's remote address.</param>
    /// <param name="headers">Request headers to send.</param>
    /// <returns>The resolver's verdict.</returns>
    private static ForwardedClient Resolve(IPAddress peer, params (string Name, string Value)[] headers)
    {
        HeaderDictionary dictionary = new HeaderDictionary();

        foreach ((string name, string value) in headers)
        {
            dictionary[name] = value;
        }

        return ForwardedClientResolver.Resolve(
            peer,
            "http",
            dictionary,
            Defaults.ParseTrustedProxies(),
            Defaults.ParseCloudflareTunnelProxies());
    }
}
