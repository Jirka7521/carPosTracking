using System.Security.Cryptography;

namespace CarPosAPI.Tests;

/// <summary>
/// Shared RSA-3072 key pairs for crypto tests. Generating a 3072-bit key takes
/// seconds, so each is created once per test run and never disposed (the
/// process end reclaims them) — tests must not wrap these in <c>using</c>.
/// </summary>
internal static class TestKeys
{
    private static readonly Lazy<RSA> s_receiverKey = new Lazy<RSA>(static () => RSA.Create(3072));

    private static readonly Lazy<RSA> s_unrelatedKey = new Lazy<RSA>(static () => RSA.Create(3072));

    /// <summary>The "device receiver" key pair — the one envelopes are built for.</summary>
    public static RSA ReceiverKey => s_receiverKey.Value;

    /// <summary>A different key pair, for wrong-key negative tests.</summary>
    public static RSA UnrelatedKey => s_unrelatedKey.Value;
}
