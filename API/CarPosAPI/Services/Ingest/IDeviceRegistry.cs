namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Resolves an MQTT topic device id to a cached, decryption-ready
/// <see cref="DeviceKeyEntry"/> — or to nothing, because unregistered devices
/// are rejected (never auto-registered).
/// </summary>
internal interface IDeviceRegistry
{
    /// <summary>Looks up an active device with usable key material.</summary>
    /// <param name="deviceId">Device id from the validated topic segment.</param>
    /// <param name="cancellationToken">Cancels the underlying database load.</param>
    /// <returns>The cached entry, or null for unknown/inactive/keyless devices.</returns>
    Task<DeviceKeyEntry?> TryGetAsync(string deviceId, CancellationToken cancellationToken);
}
