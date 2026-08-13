using System.Security.Cryptography;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// A cached, ready-to-use decryption identity for one device, produced by
/// <see cref="DeviceRegistry"/>. Holds the imported RSA-3072 private key in
/// memory for the cache lifetime — a deliberate trade-off: the key is used for
/// every single fix, so re-importing per message would buy nothing but latency.
/// Never log or serialize this type.
/// </summary>
internal sealed class DeviceKeyEntry : IDisposable
{
    /// <summary>Database primary key of the device row (FK for position inserts).</summary>
    public required Guid Id { get; init; }

    /// <summary>The device's MQTT identity (topic segment), e.g. <c>GNSS01</c>.</summary>
    public required string DeviceId { get; init; }

    /// <summary>The imported receiver private key.</summary>
    public required RSA PrivateKey { get; init; }

    /// <summary>When this entry was loaded (UTC) — drives cache refresh.</summary>
    public required DateTime LoadedAtUtc { get; init; }

    /// <summary>
    /// Serialises access to <see cref="PrivateKey"/>: RSA instance members are not
    /// guaranteed thread-safe. Message handling is sequential today, so this lock
    /// is a guard rail for future concurrency, not a hot path.
    /// </summary>
    public object DecryptLock { get; } = new object();

    /// <summary>Disposes the held RSA key material.</summary>
    public void Dispose()
    {
        PrivateKey.Dispose();
    }
}
