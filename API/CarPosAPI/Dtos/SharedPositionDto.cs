namespace CarPosAPI.Dtos;

/// <summary>
/// One fix as an anonymous visitor sees it — a deliberately impoverished cousin of
/// <see cref="PositionDto"/>.
///
/// <para>
/// <b>Every field missing from here was removed on purpose, and the list is the
/// point.</b> No <c>Id</c>: a surrogate key is a row count, and a visitor who can
/// see two of them learns how much history exists outside their window. No
/// <c>DeviceId</c>: that is the MQTT topic name, and it is exact-case for a reason
/// — combined with the broker address it is most of what you need to go looking
/// for the device itself. No <c>ReceivedAt</c>: the gap between fix and delivery
/// describes the tracker's connectivity, which is nobody's business here. No
/// altitude and no accelerometer: they answer nothing a person asking "where is
/// it" is asking, and the accelerometer in particular describes how the vehicle is
/// being driven.
/// </para>
///
/// <para>
/// The three optional fields are null unless the creator opted in, decided inside
/// the SQL projection rather than blanked out afterwards — so a column the share
/// does not cover is never read from the table at all, let alone serialised.
/// <c>ShareViewQueryTranslationTests</c> asserts that against the generated SQL,
/// and <c>SharedPositionShapeTests</c> fails the build if this record grows a
/// field.
/// </para>
/// </summary>
/// <param name="Timestamp">The GNSS fix time (UTC), named as in <see cref="PositionDto"/>.</param>
/// <param name="Latitude">Decimal degrees, +N/−S.</param>
/// <param name="Longitude">Decimal degrees, +E/−W.</param>
/// <param name="SpeedKmph">Ground speed in km/h, or null when the share does not include speed.</param>
/// <param name="BatteryPct">
/// Battery state of charge 0–100, or null when the share does not include
/// telemetry. As elsewhere, 0 is the "charging" sentinel.
/// </param>
/// <param name="TemperatureC">Modem die temperature in °C, or null when the share does not include telemetry.</param>
public sealed record SharedPositionDto(
    DateTime Timestamp,
    double Latitude,
    double Longitude,
    double? SpeedKmph,
    int? BatteryPct,
    double? TemperatureC);
