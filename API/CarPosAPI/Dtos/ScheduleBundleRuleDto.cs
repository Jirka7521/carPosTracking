using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// One weekly window as it appears inside <see cref="DeviceScheduleBundleDto"/>.
///
/// <para>
/// A near-copy of <see cref="Data.Entities.DeviceConfigScheduleRule"/> with two
/// substitutions, both made so the device can reproduce
/// <see cref="Services.Scheduling.ScheduleEvaluator"/>'s answers exactly:
/// </para>
///
/// <list type="bullet">
/// <item><description>
/// <see cref="ProfileSlot"/> instead of the profile's Guid — see
/// <see cref="ScheduleBundleProfileDto.Slot"/>.
/// </description></item>
/// <item><description>
/// <see cref="Ordinal"/> instead of <c>CreatedAt</c> and the rule's own Guid. The
/// evaluator breaks a priority tie by taking the older rule, falling back to the id;
/// shipping a rank the server has already computed from those two lets the device
/// apply the same rule with one integer comparison, and without ever seeing a
/// timestamp whose parsing could disagree.
/// </description></item>
/// </list>
///
/// <para>
/// Disabled rules are not included at all. The device has no use for a window that
/// cannot open, and the evaluator's contract has always been that the caller filters
/// them out.
/// </para>
/// </summary>
/// <param name="ProfileSlot">Which profile the window selects.</param>
/// <param name="DaysMaskUtc">7-bit weekday mask, bit 0 = Sunday.</param>
/// <param name="StartMinuteUtc">Minute of the UTC day the window opens, 0-1439.</param>
/// <param name="DurationMinutes">Window length in minutes, end-exclusive; may wrap midnight.</param>
/// <param name="Priority">Lower wins where two windows overlap.</param>
/// <param name="Ordinal">Tie-break rank among this device's rules; lower is older, and wins.</param>
public sealed record ScheduleBundleRuleDto(
    [property: JsonPropertyName("slot")] int ProfileSlot,
    [property: JsonPropertyName("days")] int DaysMaskUtc,
    [property: JsonPropertyName("start_m")] int StartMinuteUtc,
    [property: JsonPropertyName("dur_m")] int DurationMinutes,
    [property: JsonPropertyName("prio")] int Priority,
    [property: JsonPropertyName("ord")] int Ordinal);
