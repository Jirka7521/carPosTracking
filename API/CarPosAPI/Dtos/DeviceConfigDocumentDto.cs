using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The settings document exactly as it goes onto <c>devices/&lt;id&gt;/config</c>.
///
/// <para>
/// The field names are fixed by the firmware's <c>SettingsCodec</c>
/// (<c>ESP32/src/settings/SettingsCodec.cpp</c>), which is the single decoder for both
/// the MQTT message and the copy the device caches on its SD card — so this shape must
/// match it character for character. Hence the explicit
/// <see cref="JsonPropertyNameAttribute"/> on every member, the same convention
/// <see cref="PositionPayloadDto"/> and <see cref="DeliveryAckDto"/> follow for the
/// other two firmware-owned formats.
/// </para>
///
/// <para>
/// Kept separate from <see cref="DeviceConfigValuesDto"/> on purpose: that one is the
/// dashboard's camelCase view and may evolve with the UI, while this one may only
/// change in lockstep with a firmware release.
/// </para>
///
/// <para>
/// Unlike telemetry this document is <b>plaintext</b>. It carries no position data, so
/// there is nothing to protect end to end; the broker hop is still TLS.
/// </para>
/// </summary>
/// <param name="Version">Revision number the device echoes back in every report.</param>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep between reports.</param>
/// <param name="FixTimeoutSeconds">GNSS acquire budget in seconds.</param>
/// <param name="QueueMaxFixes">Undelivered-fix queue cap.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a rejected fix.</param>
/// <param name="RetryMaxAgeHours">Hours before a rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">Seconds between the device's periodic re-checks.</param>
public sealed record DeviceConfigDocumentDto(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("interval_s")] int IntervalSeconds,
    [property: JsonPropertyName("sleep_between")] bool SleepBetween,
    [property: JsonPropertyName("fix_timeout_s")] int FixTimeoutSeconds,
    [property: JsonPropertyName("queue_max_fixes")] int QueueMaxFixes,
    [property: JsonPropertyName("retry_interval_h")] int RetryIntervalHours,
    [property: JsonPropertyName("retry_max_age_h")] int RetryMaxAgeHours,
    [property: JsonPropertyName("config_check_s")] int ConfigCheckSeconds);
