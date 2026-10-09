using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The three per-mode runtime settings as the firmware reads them — the shape of the
/// <c>motion.moving</c> object inside <see cref="DeviceMotionDocumentDto"/>.
///
/// <para>
/// The three keys are <b>identical</b> to the top-level ones in
/// <see cref="DeviceConfigDocumentDto"/>. That is deliberate: the firmware decodes a
/// mode's settings with the same code whether they sit at the top of the document
/// (STANDBY) or under <c>motion.moving</c> (MOVING), so one decoder and one set of
/// clamps serve both and there is no second place for the two to drift apart. Hence the
/// explicit <see cref="JsonPropertyNameAttribute"/> on every member, the same convention
/// <see cref="DeviceConfigDocumentDto"/> follows.
/// </para>
///
/// <para>
/// Bounds are the standby ones in <see cref="DeviceConfigRules"/> — a setting means
/// the same thing in either mode. The queue cap, both retry settings and the config
/// re-check are deliberately absent: they are shared by both modes and exist only at
/// the top level, so a mode switch can never be what trims the queue or abandons a
/// rejected fix.
/// </para>
/// </summary>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep between reports.</param>
/// <param name="FixTimeoutSeconds">GNSS acquire budget in seconds.</param>
public sealed record DeviceModeDocumentDto(
    [property: JsonPropertyName("interval_s")] int IntervalSeconds,
    [property: JsonPropertyName("sleep_between")] bool SleepBetween,
    [property: JsonPropertyName("fix_timeout_s")] int FixTimeoutSeconds);
