namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Why a decrypted payload was rejected by <see cref="PositionValidator"/>.
/// Logged (and aggregated per message) instead of coordinates so diagnostics
/// never leak location data.
/// </summary>
internal enum PositionRejectReason
{
    /// <summary>Not rejected.</summary>
    None = 0,

    /// <summary>The decrypted bytes are not the expected JSON object.</summary>
    JsonInvalid,

    /// <summary>One of the six required fields is missing.</summary>
    MissingField,

    /// <summary>A numeric field is NaN or infinite.</summary>
    NonFiniteNumber,

    /// <summary>Latitude outside [-90, 90].</summary>
    LatitudeOutOfRange,

    /// <summary>Longitude outside [-180, 180].</summary>
    LongitudeOutOfRange,

    /// <summary>Speed outside [0, 1000] km/h.</summary>
    SpeedOutOfRange,

    /// <summary>Altitude outside [-500, 10000] m.</summary>
    AltitudeOutOfRange,

    /// <summary>battery_pct present but outside [0, 100].</summary>
    BatteryOutOfRange,

    /// <summary>An accel_[xyz]_g value present but non-finite or outside [-16, 16] g.</summary>
    AccelOutOfRange,

    /// <summary>time_utc does not match the firmware's exact ISO-8601 format.</summary>
    TimestampInvalid,

    /// <summary>Fix time before the minimum or too far in the future.</summary>
    TimestampOutOfWindow,

    /// <summary>
    /// The device id inside the encrypted payload differs from the topic the
    /// message arrived on — the key cross-device integrity check: one device's
    /// credentials must not write rows for another device.
    /// </summary>
    DeviceMismatch,

    /// <summary>temp_c present but non-finite or outside [-40, 125] °C.</summary>
    TemperatureOutOfRange,
}
