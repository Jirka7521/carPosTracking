namespace CarPosAPI.Services.Ingest;

/// <summary>
/// A fix that passed every <see cref="PositionValidator"/> check — the only shape
/// <see cref="PositionWriter"/> accepts. Carrying validated primitives (with the
/// timestamp already parsed to a UTC-kind <see cref="DateTime"/>, which Npgsql
/// requires for timestamptz) keeps "was this checked?" answerable by type.
/// </summary>
/// <param name="DeviceId">The validated device id (equals the topic segment).</param>
/// <param name="FixTimeUtc">Fix time, <see cref="DateTimeKind.Utc"/> guaranteed.</param>
/// <param name="Latitude">Latitude in decimal degrees, within [-90, 90].</param>
/// <param name="Longitude">Longitude in decimal degrees, within [-180, 180].</param>
/// <param name="SpeedKmph">Speed in km/h, within [0, 1000].</param>
/// <param name="AltitudeMeters">Altitude in metres, within [-500, 10000].</param>
/// <param name="BatteryPct">Battery 0–100 (0 = charging), or null when the device sent none.</param>
/// <param name="AccelXG">X-axis acceleration in g within [-16, 16], or null when absent.</param>
/// <param name="AccelYG">Y-axis acceleration in g within [-16, 16], or null when absent.</param>
/// <param name="AccelZG">Z-axis acceleration in g within [-16, 16], or null when absent.</param>
/// <param name="TemperatureC">Modem die temperature in °C within [-40, 125], or null when absent.</param>
/// <param name="SettingsVersion">
/// Settings revision the device was running when it took this fix, or null when it
/// sent none. Unlike every other member this is not a column of <c>positions</c>:
/// <see cref="PositionWriter"/> uses it to advance the <em>device</em> row's applied
/// version, and only from the newest fix in a batch. It is carried here rather than
/// beside the batch because a backlog drain mixes fixes taken under different
/// revisions, so the value only means anything attached to its own fix time.
/// </param>
internal sealed record ValidatedPosition(
    string DeviceId,
    DateTime FixTimeUtc,
    double Latitude,
    double Longitude,
    double SpeedKmph,
    double AltitudeMeters,
    int? BatteryPct,
    double? AccelXG,
    double? AccelYG,
    double? AccelZG,
    double? TemperatureC,
    int? SettingsVersion);
