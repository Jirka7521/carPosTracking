namespace CarPosAPI.Services.Ingest;

/// <summary>
/// A status message that passed <see cref="DeviceStatusValidator"/>: already classified,
/// with every optional field either sane or null. Ready for
/// <see cref="IDeviceStatusWriter"/>.
/// </summary>
/// <param name="IsOnline">True for an "I have just connected" message.</param>
/// <param name="Event">
/// The row to store, or null when the message records nothing — an ordinary online
/// message after a deep-sleep wake only moves the device's online time.
/// </param>
/// <param name="DeviceTimeUtc">The device's clock, when it sent a plausible one.</param>
/// <param name="BatteryPct">Battery percent 0–100 (0 = charging), or null.</param>
/// <param name="SleepSeconds">Expected time away in seconds, or null.</param>
/// <param name="Detail">Error detail code, or null.</param>
internal sealed record ValidatedDeviceStatus(
    bool IsOnline,
    DeviceEventClassification? Event,
    DateTime? DeviceTimeUtc,
    int? BatteryPct,
    int? SleepSeconds,
    string? Detail);
