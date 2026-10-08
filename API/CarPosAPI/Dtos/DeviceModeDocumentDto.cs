using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// One complete set of the seven runtime settings as the firmware reads it — the
/// shape of the <c>motion.moving</c> object inside <see cref="DeviceMotionDocumentDto"/>.
///
/// <para>
/// The seven keys are <b>identical</b> to the top-level ones in
/// <see cref="DeviceConfigDocumentDto"/>, minus the <c>version</c> a nested set has no
/// use for. That is deliberate: the firmware decodes a mode's settings with the same
/// code whether they sit at the top of the document (STANDBY) or under
/// <c>motion.moving</c> (MOVING), so one decoder and one set of clamps serve both and
/// there is no second place for the two to drift apart. Hence the explicit
/// <see cref="JsonPropertyNameAttribute"/> on every member, the same convention
/// <see cref="DeviceConfigDocumentDto"/> follows.
/// </para>
///
/// <para>
/// Bounds are the standby ones in <see cref="DeviceConfigRules"/> — a setting means
/// the same thing in either mode. Two firmware rules keep a mode switch from losing
/// data: the queue cap in force is the LARGER of the two sets, and the rejected-fix
/// give-up age is the more lenient (0, meaning never, beats any number).
/// </para>
/// </summary>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep between reports.</param>
/// <param name="FixTimeoutSeconds">GNSS acquire budget in seconds.</param>
/// <param name="QueueMaxFixes">Undelivered-fix queue cap.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a rejected fix.</param>
/// <param name="RetryMaxAgeHours">Hours before a rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">Seconds between the device's periodic re-checks.</param>
public sealed record DeviceModeDocumentDto(
    [property: JsonPropertyName("interval_s")] int IntervalSeconds,
    [property: JsonPropertyName("sleep_between")] bool SleepBetween,
    [property: JsonPropertyName("fix_timeout_s")] int FixTimeoutSeconds,
    [property: JsonPropertyName("queue_max_fixes")] int QueueMaxFixes,
    [property: JsonPropertyName("retry_interval_h")] int RetryIntervalHours,
    [property: JsonPropertyName("retry_max_age_h")] int RetryMaxAgeHours,
    [property: JsonPropertyName("config_check_s")] int ConfigCheckSeconds);
