using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// One profile as it appears inside <see cref="DeviceScheduleBundleDto"/>.
///
/// <para>
/// The seven value keys are <b>deliberately identical</b> to those of
/// <see cref="DeviceConfigDocumentDto"/>. That is not incidental tidiness: it lets the
/// firmware's <c>ScheduleCodec</c> hand each profile object straight to the existing
/// <c>SettingsCodec::decode</c>, so a profile is parsed, range-clamped and defaulted by
/// exactly the same code that handles the retained config document. One decoder, one
/// set of bounds, no second place for the two to drift apart.
/// </para>
///
/// <para>
/// <see cref="Slot"/> replaces the profile's Guid on the wire. Twelve profile Guids
/// plus the rules that reference them would be well over a kilobyte of pure
/// identifier, and — more to the point — the slot is echoed back inside <em>every</em>
/// encrypted GNSS fix the device sends. A 36-character string there, for ever, to say
/// something a single byte says exactly as well.
/// </para>
/// </summary>
/// <param name="Slot">Stable per-device index, 0-based; see <c>DeviceConfigProfile.ScheduleSlot</c>.</param>
/// <param name="Name">The profile's name. Carried for the device's serial log only — nothing on the device keys off it.</param>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep between reports.</param>
/// <param name="FixTimeoutSeconds">GNSS acquire budget in seconds.</param>
/// <param name="QueueMaxFixes">Undelivered-fix queue cap.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a rejected fix.</param>
/// <param name="RetryMaxAgeHours">Hours before a rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">Seconds between the device's periodic re-checks.</param>
public sealed record ScheduleBundleProfileDto(
    [property: JsonPropertyName("slot")] int Slot,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("interval_s")] int IntervalSeconds,
    [property: JsonPropertyName("sleep_between")] bool SleepBetween,
    [property: JsonPropertyName("fix_timeout_s")] int FixTimeoutSeconds,
    [property: JsonPropertyName("queue_max_fixes")] int QueueMaxFixes,
    [property: JsonPropertyName("retry_interval_h")] int RetryIntervalHours,
    [property: JsonPropertyName("retry_max_age_h")] int RetryMaxAgeHours,
    [property: JsonPropertyName("config_check_s")] int ConfigCheckSeconds);
