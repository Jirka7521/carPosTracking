using System.Net;
using CarPosAPI.Services.Health;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins who gets the full <c>/health</c> report. The tunnel publishes the endpoint,
/// so anything this lets through is published too.
/// </summary>
public sealed class HealthDetailPolicyTests
{
    private static readonly IPAddress Container = IPAddress.Parse("192.168.124.5");

    [Fact]
    public void LoopbackIsInternal()
    {
        Assert.True(HealthDetailPolicy.IsInternal(IPAddress.Loopback, IPAddress.Loopback, viaTrustedProxy: false));
        Assert.True(HealthDetailPolicy.IsInternal(IPAddress.IPv6Loopback, IPAddress.IPv6Loopback, viaTrustedProxy: false));
    }

    [Fact]
    public void TheContainerProbingItsOwnAddressIsInternal()
    {
        // The deployment's healthcheck: wget $BE_URL/health from inside the API
        // container, which the network alias resolves back to the container itself.
        // Kestrel reports both ends IPv4-mapped.
        Assert.True(HealthDetailPolicy.IsInternal(Container.MapToIPv6(), Container.MapToIPv6(), viaTrustedProxy: false));
    }

    [Fact]
    public void AnotherHostIsNot()
    {
        Assert.False(HealthDetailPolicy.IsInternal(IPAddress.Parse("192.168.124.77"), Container, viaTrustedProxy: false));
        Assert.False(HealthDetailPolicy.IsInternal(null, Container, viaTrustedProxy: false));
    }

    [Fact]
    public void NothingThatCameThroughAProxyIsInternal()
    {
        // Whatever the proxy reported, and even if it is on the same host: the tunnel
        // routes the public internet through exactly such a peer.
        Assert.False(HealthDetailPolicy.IsInternal(IPAddress.Loopback, IPAddress.Loopback, viaTrustedProxy: true));
    }

    [Fact]
    public void AWildcardLocalAddressMatchesNothing()
    {
        Assert.False(HealthDetailPolicy.IsInternal(IPAddress.Any, IPAddress.Any, viaTrustedProxy: false));
    }
}
