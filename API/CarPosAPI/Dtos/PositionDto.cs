namespace CarPosAPI.Dtos;

/// <summary>
/// One GNSS fix on the wire, projected straight from
/// <see cref="Data.Entities.Position"/> in the query.
/// </summary>
/// <param name="Id">Surrogate key; the React list key.</param>
/// <param name="DeviceId">MQTT identity of the reporting device.</param>
/// <param name="Timestamp">
/// The GNSS fix time (UTC) — <see cref="Data.Entities.Position.FixTime"/>. Named
/// "timestamp" on the wire because that is what it means to a reader of the map:
/// when the vehicle was there, not when the server heard about it. The two differ
/// by hours when a backlog is replayed, which is what
/// <paramref name="ReceivedAt"/> is for.
/// </param>
/// <param name="ReceivedAt">When the server stored the fix (UTC).</param>
/// <param name="Latitude">Decimal degrees, +N/−S.</param>
/// <param name="Longitude">Decimal degrees, +E/−W.</param>
/// <param name="SpeedKmph">Ground speed in km/h as reported by the receiver.</param>
/// <param name="AltitudeMeters">Altitude above mean sea level in metres.</param>
/// <param name="BatteryPct">
/// Battery state of charge 0–100, or null when the device reported none. The
/// value 0 is the "charging" sentinel — the FE renders it as charging.
/// </param>
/// <param name="AccelXG">X-axis acceleration in g at the fix, or null when absent.</param>
/// <param name="AccelYG">Y-axis acceleration in g at the fix, or null when absent.</param>
/// <param name="AccelZG">Z-axis acceleration in g at the fix, or null when absent.</param>
/// <param name="TemperatureC">
/// Modem die temperature in °C at the fix, or null when the device reported none.
/// </param>
public sealed record PositionDto(
    long Id,
    string DeviceId,
    DateTime Timestamp,
    DateTime ReceivedAt,
    double Latitude,
    double Longitude,
    double SpeedKmph,
    double AltitudeMeters,
    int? BatteryPct,
    double? AccelXG,
    double? AccelYG,
    double? AccelZG,
    double? TemperatureC);
