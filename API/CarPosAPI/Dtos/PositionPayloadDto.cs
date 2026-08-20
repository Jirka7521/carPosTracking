using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The decrypted inner position payload the firmware serialises
/// (<c>ESP32/src/mqtt/TelemetryPublisher.cpp</c>). The six location fields are
/// always present; the battery and accelerometer fields are optional — the
/// firmware omits them when that sensor is disabled or a read failed, and an
/// older firmware sends none of them at all. Every member is nullable so absence
/// is a structured decision in <see cref="Services.Ingest.PositionValidator"/>
/// (a required field missing is a rejection; an optional sensor field missing is
/// simply stored as null) rather than a serializer crash. This DTO carries
/// location data (personal data) — it must never be logged.
/// </summary>
/// <param name="Device">Device id claimed inside the encrypted payload; must match the topic.</param>
/// <param name="LatitudeDeg">Latitude in decimal degrees (+N/−S).</param>
/// <param name="LongitudeDeg">Longitude in decimal degrees (+E/−W).</param>
/// <param name="SpeedKmph">Ground speed in km/h.</param>
/// <param name="AltitudeMeters">Altitude above mean sea level in metres.</param>
/// <param name="TimeUtc">Fix time as ISO-8601 UTC with second precision, e.g. <c>2026-07-14T12:34:56Z</c>.</param>
/// <param name="BatteryPct">Battery state of charge 0–100; the sentinel 0 means "charging" (optional).</param>
/// <param name="AccelXG">Instantaneous X-axis acceleration in g (optional).</param>
/// <param name="AccelYG">Instantaneous Y-axis acceleration in g (optional).</param>
/// <param name="AccelZG">Instantaneous Z-axis acceleration in g (optional).</param>
/// <param name="TempC">Modem die temperature in °C from AT+CPMUTEMP (optional).</param>
/// <param name="SettingsVersion">
/// Revision of the settings document the device was running when it took this fix
/// (optional — absent on firmware that predates remote settings, and on a device that
/// has never received a config). It is not stored with the position; it advances the
/// device row's <c>config_applied_version</c>, which is how the dashboard knows a
/// published change has actually been picked up.
/// </param>
public sealed record PositionPayloadDto(
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("latitude_deg")] double? LatitudeDeg,
    [property: JsonPropertyName("longitude_deg")] double? LongitudeDeg,
    [property: JsonPropertyName("speed_kmph")] double? SpeedKmph,
    [property: JsonPropertyName("altitude_m")] double? AltitudeMeters,
    [property: JsonPropertyName("time_utc")] string? TimeUtc,
    [property: JsonPropertyName("battery_pct")] int? BatteryPct = null,
    [property: JsonPropertyName("accel_x_g")] double? AccelXG = null,
    [property: JsonPropertyName("accel_y_g")] double? AccelYG = null,
    [property: JsonPropertyName("accel_z_g")] double? AccelZG = null,
    [property: JsonPropertyName("temp_c")] double? TempC = null,
    [property: JsonPropertyName("settings_version")] int? SettingsVersion = null);
