using System.Collections.Concurrent;
using System.Security.Cryptography;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Options;
using CarPosAPI.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Singleton cache mapping device ids to decryption-ready
/// <see cref="DeviceKeyEntry"/>s. Loads the device row via the context factory,
/// unprotects the private-key PEM with the master key and imports it once —
/// paying the database query and RSA import per cache lifetime instead of per
/// fix. Two TTLs bound staleness: a positive TTL (device deactivation or key
/// rotation takes effect without a restart) and a negative TTL (an attacker
/// publishing to invented topics cannot turn every message into a DB query).
/// Entries are refreshed in-line by the strictly sequential pipeline, so an old
/// RSA instance is never disposed while another message is using it.
/// </summary>
internal sealed class DeviceRegistry : IDeviceRegistry, IDisposable
{
    /// <summary>The only key size the firmware ecosystem uses.</summary>
    private const int ExpectedRsaKeySizeBits = 3072;

    private readonly IDbContextFactory<CarPosDbContext> _contextFactory;
    private readonly IMasterKeyProtector _protector;
    private readonly IngestOptions _options;
    private readonly ILogger<DeviceRegistry> _logger;

    private readonly ConcurrentDictionary<string, DeviceKeyEntry> _entries =
        new ConcurrentDictionary<string, DeviceKeyEntry>(StringComparer.Ordinal);

    /// <summary>Device id → UTC expiry of its "not usable" verdict.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _negativeCache =
        new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

    /// <summary>Creates the registry.</summary>
    /// <param name="contextFactory">Factory for short-lived DbContexts (singleton-safe).</param>
    /// <param name="protector">Master-key decryptor for stored private keys.</param>
    /// <param name="options">Cache TTL configuration.</param>
    /// <param name="logger">Structured logger (never receives key material).</param>
    public DeviceRegistry(
        IDbContextFactory<CarPosDbContext> contextFactory,
        IMasterKeyProtector protector,
        IOptions<IngestOptions> options,
        ILogger<DeviceRegistry> logger)
    {
        _contextFactory = contextFactory;
        _protector = protector;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DeviceKeyEntry?> TryGetAsync(string deviceId, CancellationToken cancellationToken)
    {
        DateTime utcNow = DateTime.UtcNow;

        if (_entries.TryGetValue(deviceId, out DeviceKeyEntry? cached))
        {
            if (utcNow < cached.LoadedAtUtc.AddMinutes(_options.DeviceCacheRefreshMinutes))
            {
                return cached;
            }

            // Expired: drop and reload. Disposing here is safe because the MQTT
            // pipeline processes messages one at a time (no concurrent user).
            _entries.TryRemove(deviceId, out DeviceKeyEntry? _);
            cached.Dispose();
        }

        if (_negativeCache.TryGetValue(deviceId, out DateTime negativeExpiry))
        {
            if (utcNow < negativeExpiry)
            {
                return null;
            }

            _negativeCache.TryRemove(deviceId, out DateTime _);
        }

        DeviceKeyEntry? loaded = await LoadAsync(deviceId, utcNow, cancellationToken);
        if (loaded is null)
        {
            _negativeCache[deviceId] = utcNow.AddMinutes(_options.UnknownDeviceNegativeCacheMinutes);
            return null;
        }

        _entries[deviceId] = loaded;
        return loaded;
    }

    /// <summary>Loads one device row and prepares its RSA key.</summary>
    /// <param name="deviceId">The MQTT device id.</param>
    /// <param name="utcNow">Load timestamp stamped into the entry.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The entry, or null when the device cannot be used for ingest.</returns>
    private async Task<DeviceKeyEntry?> LoadAsync(string deviceId, DateTime utcNow, CancellationToken cancellationToken)
    {
        await using CarPosDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        Device? device = await context.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.DeviceId == deviceId && candidate.IsActive,
                cancellationToken);

        if (device is null)
        {
            _logger.LogWarning("Rejecting message for unknown or inactive device {DeviceId}", deviceId);
            return null;
        }

        if (device.PrivateKeyCiphertext is null)
        {
            _logger.LogWarning("Device {DeviceId} has no private key provisioned — run import-device-key", deviceId);
            return null;
        }

        string privateKeyPem;
        try
        {
            privateKeyPem = _protector.Unprotect(device.PrivateKeyCiphertext, device.DeviceId);
        }
        catch (CryptographicException)
        {
            // Wrong master key or corrupt row — an operator problem, so log loudly,
            // but never the exception content (it concerns key material).
            _logger.LogError(
                "Failed to unprotect the private key for device {DeviceId} — wrong master key or corrupt ciphertext",
                deviceId);
            return null;
        }

        RSA privateKey = RSA.Create();
        try
        {
            privateKey.ImportFromPem(privateKeyPem);
            if (privateKey.KeySize != ExpectedRsaKeySizeBits)
            {
                _logger.LogError(
                    "Device {DeviceId} key size {KeySize} is not the expected {ExpectedKeySize}",
                    deviceId,
                    privateKey.KeySize,
                    ExpectedRsaKeySizeBits);
                privateKey.Dispose();
                return null;
            }
        }
        catch (ArgumentException)
        {
            _logger.LogError("Stored private key for device {DeviceId} is not a valid PEM", deviceId);
            privateKey.Dispose();
            return null;
        }
        catch (CryptographicException)
        {
            _logger.LogError("Stored private key for device {DeviceId} could not be imported", deviceId);
            privateKey.Dispose();
            return null;
        }

        // The ack key is strictly optional: a device without one still has its fixes
        // ingested, it just never hears back. So an absent or unusable ack key
        // degrades to "acks off for this device" rather than failing the load —
        // refusing telemetry over a broken *reply* path would be the wrong trade.
        RSA? ackPublicKey = TryImportAckPublicKey(device);

        _logger.LogInformation(
            "Loaded decryption key for device {DeviceId} (delivery acks {AckState})",
            deviceId,
            ackPublicKey is null ? "disabled" : "enabled");
        return new DeviceKeyEntry
        {
            Id = device.Id,
            DeviceId = device.DeviceId,
            PrivateKey = privateKey,
            AckPublicKey = ackPublicKey,
            LoadedAtUtc = utcNow,
        };
    }

    /// <summary>
    /// Imports the device's ack public key, if one is provisioned. Never throws:
    /// every failure is logged and returns null, which disables acks for the device.
    /// </summary>
    /// <param name="device">The loaded device row.</param>
    /// <returns>The imported public key, or null when absent or unusable.</returns>
    private RSA? TryImportAckPublicKey(Device device)
    {
        if (string.IsNullOrWhiteSpace(device.AckPublicKeyPem))
        {
            return null;
        }

        RSA ackPublicKey = RSA.Create();
        try
        {
            ackPublicKey.ImportFromPem(device.AckPublicKeyPem);
            if (ackPublicKey.KeySize != ExpectedRsaKeySizeBits)
            {
                _logger.LogError(
                    "Device {DeviceId} ack key size {KeySize} is not the expected {ExpectedKeySize} — acks disabled",
                    device.DeviceId,
                    ackPublicKey.KeySize,
                    ExpectedRsaKeySizeBits);
                ackPublicKey.Dispose();
                return null;
            }

            return ackPublicKey;
        }
        catch (ArgumentException)
        {
            _logger.LogError("Stored ack public key for device {DeviceId} is not a valid PEM — acks disabled", device.DeviceId);
            ackPublicKey.Dispose();
            return null;
        }
        catch (CryptographicException)
        {
            _logger.LogError("Stored ack public key for device {DeviceId} could not be imported — acks disabled", device.DeviceId);
            ackPublicKey.Dispose();
            return null;
        }
    }

    /// <summary>Disposes all cached RSA instances on application shutdown.</summary>
    public void Dispose()
    {
        foreach (KeyValuePair<string, DeviceKeyEntry> pair in _entries)
        {
            pair.Value.Dispose();
        }

        _entries.Clear();
    }
}
