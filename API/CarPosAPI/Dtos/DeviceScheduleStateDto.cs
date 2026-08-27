namespace CarPosAPI.Dtos;

/// <summary>
/// Everything the schedule panel renders, in one response.
///
/// <para>
/// One document rather than five endpoints because the parts are only meaningful
/// together: a rule list without its profiles shows Guids, a status without its rules
/// cannot be checked by the reader, and an override without the status cannot say what
/// it is holding off. Every mutation returns this same shape for the same reason —
/// after adding a rule the "next switch" has moved, and a client that had to fetch it
/// separately would render a stale one in between.
/// </para>
/// </summary>
/// <param name="Enabled">Whether the rules are actually driving this device's settings.</param>
/// <param name="FallbackProfileId">Applied wherever no window covers the moment; null when unset.</param>
/// <param name="Profiles">Every profile this device has, name-ordered.</param>
/// <param name="Rules">Every rule, in evaluation order — priority, then age.</param>
/// <param name="Status">
/// What the schedule resolves to now. Null while <paramref name="Enabled"/> is false,
/// because nothing is acting on the rules and a confident answer would be fiction.
/// </param>
/// <param name="Override">A live manual override, or null when the schedule is in charge.</param>
/// <param name="EvaluatedAt">
/// When the scheduler last completed a pass over this device (UTC). Null means it never
/// has — which, on an enabled schedule, is how the dashboard can say "waiting for the
/// scheduler" rather than showing an answer nobody has acted on yet.
/// </param>
public sealed record DeviceScheduleStateDto(
    bool Enabled,
    Guid? FallbackProfileId,
    IReadOnlyList<DeviceConfigProfileDto> Profiles,
    IReadOnlyList<DeviceScheduleRuleDto> Rules,
    DeviceScheduleStatusDto? Status,
    DeviceScheduleOverrideDto? Override,
    DateTime? EvaluatedAt);
