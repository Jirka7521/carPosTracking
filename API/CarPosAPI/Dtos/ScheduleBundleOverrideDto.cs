using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// A set of values that beats the schedule until a stated instant, as it appears
/// inside <see cref="DeviceScheduleBundleDto"/>. Omitted from the bundle entirely when
/// there is none.
///
/// <para>
/// Two things produce one. A person saving the settings form on a scheduled device
/// gets an override until the next switch — that is the existing
/// <c>Device.ConfigOverrideUntil</c> behaviour, and the dashboard already tells them
/// "this change is temporary". The reconciler produces the other: when a device is
/// running a profile the schedule disagrees with, the correction has to be able to
/// beat the device's own evaluation, and an override is how it does that.
/// </para>
///
/// <para>
/// Both self-expire, which is the point. A flag somebody forgot to clear could strand
/// a tracker on the wrong settings for ever; an instant cannot. Once it passes, the
/// device is back under its own schedule with no further message from anyone.
/// </para>
///
/// <para>
/// <see cref="UntilUtc"/> is a <b>string</b>, not a <see cref="DateTime"/>, on
/// purpose. The firmware parses it with <c>CivilTime::parseIso</c>, which accepts
/// exactly <c>YYYY-MM-DDTHH:MM:SSZ</c> and nothing else — twenty characters, no
/// fractional seconds, no offset. Serializing a <see cref="DateTime"/> would emit the
/// round-trip format with a fractional part, which that parser rejects outright, and
/// the device would silently ignore every override. Build it with
/// <see cref="Services.Scheduling.ScheduleBundleBuilder.FormatDeviceInstant"/>.
/// </para>
/// </summary>
/// <param name="UntilUtc">When the override lapses, as <c>YYYY-MM-DDTHH:MM:SSZ</c>.</param>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep between reports.</param>
/// <param name="FixTimeoutSeconds">GNSS acquire budget in seconds.</param>
/// <param name="QueueMaxFixes">Undelivered-fix queue cap.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a rejected fix.</param>
/// <param name="RetryMaxAgeHours">Hours before a rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">Seconds between the device's periodic re-checks.</param>
public sealed record ScheduleBundleOverrideDto(
    [property: JsonPropertyName("until")] string UntilUtc,
    [property: JsonPropertyName("interval_s")] int IntervalSeconds,
    [property: JsonPropertyName("sleep_between")] bool SleepBetween,
    [property: JsonPropertyName("fix_timeout_s")] int FixTimeoutSeconds,
    [property: JsonPropertyName("queue_max_fixes")] int QueueMaxFixes,
    [property: JsonPropertyName("retry_interval_h")] int RetryIntervalHours,
    [property: JsonPropertyName("retry_max_age_h")] int RetryMaxAgeHours,
    [property: JsonPropertyName("config_check_s")] int ConfigCheckSeconds);
