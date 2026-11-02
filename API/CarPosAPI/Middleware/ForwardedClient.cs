using System.Net;

namespace CarPosAPI.Middleware;

/// <summary>
/// Who a request really came from, as decided by <see cref="ForwardedClientResolver"/>.
/// Stored on the request as a feature by <see cref="ForwardedClientMiddleware"/>, so
/// code that needs the raw connection — not the client a proxy vouched for — can
/// still ask for it.
/// </summary>
/// <param name="PeerAddress">The address that opened the TCP connection; null when unknown.</param>
/// <param name="ClientAddress">The client the request is attributed to: the peer itself, or the address a trusted proxy reported.</param>
/// <param name="Scheme">The scheme the client used — <c>https</c> when a trusted proxy says TLS ended there.</param>
/// <param name="ViaTrustedProxy">True when the peer is a configured proxy, whether or not it sent any header.</param>
public sealed record ForwardedClient(
    IPAddress? PeerAddress,
    IPAddress? ClientAddress,
    string Scheme,
    bool ViaTrustedProxy);
