using System.Globalization;
using System.Text.RegularExpressions;
using CarPosAPI.Data.Configurations;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Semantic validation of decrypted status payloads — the status-topic counterpart of
/// <see cref="PositionValidator"/>.
///
/// <para>
/// Strict about what makes a message meaningful (who sent it, what type, which reason),
/// lenient about everything else: an out-of-range battery or a garbled timestamp is
/// dropped to null and the event is kept. A "battery low" that arrives with a bad
/// battery figure is still a battery-low event, and losing it to a field that was only
/// ever decoration would defeat the point of having it.
/// </para>
///
/// <para>
/// Every bound mirrors a CHECK constraint in <see cref="DeviceEventConfiguration"/>, so
/// nothing that passes here can die on insert.
/// </para>
/// </summary>
internal sealed partial class DeviceStatusValidator
{
    /// <summary>Battery floor — 0 is the "charging" sentinel, as in positions.</summary>
    public const int MinBatteryPct = 0;

    /// <summary>Battery ceiling.</summary>
    public const int MaxBatteryPct = 100;

    /// <summary>
    /// How far back a device's clock may plausibly sit. A status message is published
    /// live, never replayed from the card, so anything much older is a clock gone wrong
    /// rather than a late delivery — and the receive time is what the event uses anyway.
    /// </summary>
    public const int MaxDeviceTimeAgeDays = 30;

    private readonly IngestOptions _options;

    /// <summary>Creates the validator with the configured clock-skew allowance.</summary>
    /// <param name="options">Validated ingest limits.</param>
    public DeviceStatusValidator(IOptions<IngestOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Validates and classifies one decrypted status payload.</summary>
    /// <param name="payload">The deserialized inner payload.</param>
    /// <param name="topicDeviceId">Device id taken from the MQTT topic.</param>
    /// <param name="utcNow">Current UTC time (parameter for testability).</param>
    /// <param name="status">The validated status when the method returns true.</param>
    /// <param name="reason">Why validation failed when the method returns false.</param>
    /// <returns><c>true</c> when the payload is acceptable.</returns>
    public bool TryValidate(
        DeviceStatusPayloadDto payload,
        string topicDeviceId,
        DateTime utcNow,
        out ValidatedDeviceStatus? status,
        out DeviceStatusRejectReason reason)
    {
        status = null;

        if (payload.Device is null || payload.Type is null)
        {
            reason = DeviceStatusRejectReason.MissingField;
            return false;
        }

        // Envelopes sealed for device A but replayed onto device B's status topic die
        // here, exactly as they would on the telemetry topic.
        if (!string.Equals(payload.Device, topicDeviceId, StringComparison.Ordinal))
        {
            reason = DeviceStatusRejectReason.DeviceMismatch;
            return false;
        }

        bool isOnline;
        DeviceEventClassification? classification;
        if (string.Equals(payload.Type, DeviceEventClassifier.OnlineType, StringComparison.Ordinal))
        {
            isOnline = true;
            // An unknown or routine reset cause simply records no restart.
            classification = DeviceEventClassifier.ClassifyRestart(payload.ResetReason);
        }
        else if (string.Equals(payload.Type, DeviceEventClassifier.OfflineType, StringComparison.Ordinal))
        {
            isOnline = false;
            if (payload.Reason is null)
            {
                reason = DeviceStatusRejectReason.MissingField;
                return false;
            }

            // An offline message without a reason we understand has nothing to say: the
            // dashboard could only show "offline because <something>".
            if (!DeviceEventClassifier.TryClassifyOffline(payload.Reason, out classification))
            {
                reason = DeviceStatusRejectReason.UnknownReason;
                return false;
            }
        }
        else
        {
            reason = DeviceStatusRejectReason.UnknownType;
            return false;
        }

        status = new ValidatedDeviceStatus(
            isOnline,
            classification,
            ParseDeviceTime(payload.TimeUtc, utcNow),
            payload.BatteryPct >= MinBatteryPct && payload.BatteryPct <= MaxBatteryPct
                ? payload.BatteryPct
                : null,
            // Zero means "unknown" on the device side; anything past the longest
            // reporting interval is not a sleep this firmware can take.
            payload.SleepSeconds > 0 && payload.SleepSeconds <= DeviceConfigRules.MaxIntervalSeconds
                ? payload.SleepSeconds
                : null,
            payload.Detail is not null && DetailRegex().IsMatch(payload.Detail)
                ? payload.Detail
                : null);
        reason = DeviceStatusRejectReason.None;
        return true;
    }

    /// <summary>
    /// Parses the device's optional timestamp, in exactly the firmware's shape, and
    /// keeps it only when it is plausible. Null rather than a rejection: the event's
    /// time is the server's receive time regardless.
    /// </summary>
    /// <param name="timeUtc">The raw <c>time_utc</c>, or null.</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <returns>The instant (Kind Utc), or null.</returns>
    private DateTime? ParseDeviceTime(string? timeUtc, DateTime utcNow)
    {
        if (timeUtc is null)
        {
            return null;
        }

        bool parsed = DateTime.TryParseExact(
            timeUtc,
            PositionValidator.FixTimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTime deviceTime);
        if (!parsed)
        {
            return null;
        }

        DateTime earliest = utcNow.AddDays(-MaxDeviceTimeAgeDays);
        DateTime latest = utcNow.AddMinutes(_options.MaxFutureClockSkewMinutes);
        return deviceTime >= earliest && deviceTime <= latest ? deviceTime : null;
    }

    /// <summary>The firmware's detail codes — short, lowercase, machine-readable.</summary>
    [GeneratedRegex("^[a-z0-9_]{1,32}$")]
    private static partial Regex DetailRegex();
}
