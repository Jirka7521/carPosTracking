using System.Globalization;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Semantic validation of decrypted position payloads. Decryption only proves the
/// payload was encrypted with this device's public key — it says nothing about
/// the values, so ranges, timestamp sanity and the topic-vs-payload device match
/// are enforced here before anything reaches the database. The numeric bounds
/// mirror the CHECK constraints in
/// <see cref="Data.Configurations.PositionConfiguration"/>: validator and schema
/// must agree, or valid-looking batches would die on insert.
/// </summary>
internal sealed class PositionValidator
{
    /// <summary>Inclusive latitude bounds in decimal degrees.</summary>
    public const double MinLatitude = -90.0;

    /// <summary>Inclusive latitude bounds in decimal degrees.</summary>
    public const double MaxLatitude = 90.0;

    /// <summary>Inclusive longitude bounds in decimal degrees.</summary>
    public const double MinLongitude = -180.0;

    /// <summary>Inclusive longitude bounds in decimal degrees.</summary>
    public const double MaxLongitude = 180.0;

    /// <summary>Speed floor — negative speed is a corrupt fix.</summary>
    public const double MinSpeedKmph = 0.0;

    /// <summary>Speed ceiling — no car does 1000 km/h; beyond this is GNSS noise.</summary>
    public const double MaxSpeedKmph = 1000.0;

    /// <summary>Altitude floor (below the lowest land, with margin) in metres.</summary>
    public const double MinAltitudeMeters = -500.0;

    /// <summary>Altitude ceiling (above any road, with margin) in metres.</summary>
    public const double MaxAltitudeMeters = 10000.0;

    /// <summary>Battery floor — the value 0 is the "charging" sentinel.</summary>
    public const int MinBatteryPct = 0;

    /// <summary>Battery ceiling — a percentage cannot exceed 100.</summary>
    public const int MaxBatteryPct = 100;

    /// <summary>Accel magnitude ceiling per axis — the ADXL345's widest range is ±16 g.</summary>
    public const double MaxAbsAccelG = 16.0;

    /// <summary>Temperature floor in °C — below this is not a plausible modem reading.</summary>
    public const double MinTemperatureC = -40.0;

    /// <summary>Temperature ceiling in °C — the SIM7000 die sensor never legitimately exceeds this.</summary>
    public const double MaxTemperatureC = 125.0;

    /// <summary>
    /// The firmware's exact timestamp shape (<c>TelemetryPublisher.cpp</c> emits
    /// <c>%04u-%02u-%02uT%02u:%02u:%02uZ</c>). Parsed exactly — anything looser
    /// (milliseconds, offsets) does not come from our firmware.
    /// </summary>
    public const string FixTimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private readonly IngestOptions _options;

    /// <summary>Creates the validator with configured time-window limits.</summary>
    /// <param name="options">Validated ingest limits.</param>
    public PositionValidator(IOptions<IngestOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Validates one decrypted payload.</summary>
    /// <param name="payload">The deserialized inner payload.</param>
    /// <param name="topicDeviceId">Device id taken from the MQTT topic.</param>
    /// <param name="utcNow">Current UTC time (parameter for testability).</param>
    /// <param name="position">The validated fix when the method returns true.</param>
    /// <param name="reason">Why validation failed when the method returns false.</param>
    /// <returns><c>true</c> when the payload is acceptable.</returns>
    public bool TryValidate(
        PositionPayloadDto payload,
        string topicDeviceId,
        DateTime utcNow,
        out ValidatedPosition? position,
        out PositionRejectReason reason)
    {
        position = null;

        if (payload.Device is null
            || payload.LatitudeDeg is null
            || payload.LongitudeDeg is null
            || payload.SpeedKmph is null
            || payload.AltitudeMeters is null
            || payload.TimeUtc is null)
        {
            reason = PositionRejectReason.MissingField;
            return false;
        }

        // The inner device id must match the topic the message arrived on. This is
        // the cross-device integrity check: envelopes encrypted for device A but
        // replayed onto device B's topic (or vice versa) die here.
        if (!string.Equals(payload.Device, topicDeviceId, StringComparison.Ordinal))
        {
            reason = PositionRejectReason.DeviceMismatch;
            return false;
        }

        double latitude = payload.LatitudeDeg.Value;
        double longitude = payload.LongitudeDeg.Value;
        double speedKmph = payload.SpeedKmph.Value;
        double altitudeMeters = payload.AltitudeMeters.Value;

        if (!double.IsFinite(latitude) || !double.IsFinite(longitude)
            || !double.IsFinite(speedKmph) || !double.IsFinite(altitudeMeters))
        {
            reason = PositionRejectReason.NonFiniteNumber;
            return false;
        }

        if (latitude < MinLatitude || latitude > MaxLatitude)
        {
            reason = PositionRejectReason.LatitudeOutOfRange;
            return false;
        }

        if (longitude < MinLongitude || longitude > MaxLongitude)
        {
            reason = PositionRejectReason.LongitudeOutOfRange;
            return false;
        }

        if (speedKmph < MinSpeedKmph || speedKmph > MaxSpeedKmph)
        {
            reason = PositionRejectReason.SpeedOutOfRange;
            return false;
        }

        if (altitudeMeters < MinAltitudeMeters || altitudeMeters > MaxAltitudeMeters)
        {
            reason = PositionRejectReason.AltitudeOutOfRange;
            return false;
        }

        // Battery and accelerometer are OPTIONAL: a device with those sensors
        // disabled — or older firmware that predates them — simply omits the
        // fields, and that is stored as null (never a rejection). But when a
        // value IS present it must be sane, so a corrupt reading can never reach
        // the CHECK-constrained columns.
        if (payload.BatteryPct is int batteryPct
            && (batteryPct < MinBatteryPct || batteryPct > MaxBatteryPct))
        {
            reason = PositionRejectReason.BatteryOutOfRange;
            return false;
        }

        if (!IsAccelAxisAcceptable(payload.AccelXG)
            || !IsAccelAxisAcceptable(payload.AccelYG)
            || !IsAccelAxisAcceptable(payload.AccelZG))
        {
            reason = PositionRejectReason.AccelOutOfRange;
            return false;
        }

        // Temperature is optional too (older firmware and any non-SIM7000 modem
        // omit it); when present it must be finite and within the sensor's range,
        // so a corrupt reading never reaches the CHECK-constrained column.
        if (payload.TempC is double temperatureC
            && (!double.IsFinite(temperatureC)
                || temperatureC < MinTemperatureC || temperatureC > MaxTemperatureC))
        {
            reason = PositionRejectReason.TemperatureOutOfRange;
            return false;
        }

        // AdjustToUniversal + AssumeUniversal yields DateTimeKind.Utc, which Npgsql
        // demands for timestamptz parameters — parsing and kind are settled here once.
        bool parsed = DateTime.TryParseExact(
            payload.TimeUtc,
            FixTimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTime fixTimeUtc);
        if (!parsed)
        {
            reason = PositionRejectReason.TimestampInvalid;
            return false;
        }

        // Backlogged fixes may be days old (SD-card store-and-forward), so the lower
        // bound is generous; the future bound only absorbs modest clock skew.
        DateTime maxAcceptable = utcNow.AddMinutes(_options.MaxFutureClockSkewMinutes);
        if (fixTimeUtc < _options.FixTimeMinUtc || fixTimeUtc > maxAcceptable)
        {
            reason = PositionRejectReason.TimestampOutOfWindow;
            return false;
        }

        position = new ValidatedPosition(
            payload.Device,
            fixTimeUtc,
            latitude,
            longitude,
            speedKmph,
            altitudeMeters,
            payload.BatteryPct,
            payload.AccelXG,
            payload.AccelYG,
            payload.AccelZG,
            payload.TempC,
            // Deliberately unvalidated beyond "is it a positive number". A revision the
            // server has never issued is not a reason to throw away a good position:
            // the worst case is that the dashboard shows the device as out of sync,
            // which is exactly what it would be.
            payload.SettingsVersion > 0 ? payload.SettingsVersion : null);
        reason = PositionRejectReason.None;
        return true;
    }

    /// <summary>
    /// An accelerometer axis is acceptable when it is absent (null), or present
    /// and both finite and within the ADXL345's ±16 g range.
    /// </summary>
    /// <param name="value">The axis value from the payload, possibly null.</param>
    /// <returns><c>true</c> when the axis passes; <c>false</c> rejects the fix.</returns>
    private static bool IsAccelAxisAcceptable(double? value)
    {
        if (value is not double accel)
        {
            return true;  // absent is fine — it is stored as null
        }

        return double.IsFinite(accel) && Math.Abs(accel) <= MaxAbsAccelG;
    }
}
