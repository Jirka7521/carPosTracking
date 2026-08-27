namespace CarPosAPI.Dtos;

/// <summary>
/// What the schedule resolves to right now, and what it changes to next.
///
/// <para>
/// Computed server-side and never by the client, even though the client has the rules
/// and could. The server is the thing that <em>acts</em> on this answer, and a
/// dashboard that computed its own would eventually disagree with the tracker over a
/// rounding rule or a wrap — and be believed, because it is the one on screen.
/// </para>
///
/// <para>
/// Null throughout when the schedule is off: there is no active profile to name,
/// because nothing is acting on the rules.
/// </para>
/// </summary>
/// <param name="ActiveProfileId">The profile currently in force.</param>
/// <param name="ActiveProfileName">Its name, for the banner.</param>
/// <param name="ActiveRuleId">The rule whose window matched, or null when the fallback did.</param>
/// <param name="ActiveSince">
/// When the current stretch began (UTC), or null when the schedule resolves the same
/// way all week and there is no meaningful "since".
/// </param>
/// <param name="NextChangeAt">When the active profile next changes (UTC), or null when it never does.</param>
/// <param name="NextProfileId">The profile taking over then.</param>
/// <param name="NextProfileName">Its name.</param>
public sealed record DeviceScheduleStatusDto(
    Guid? ActiveProfileId,
    string? ActiveProfileName,
    Guid? ActiveRuleId,
    DateTime? ActiveSince,
    DateTime? NextChangeAt,
    Guid? NextProfileId,
    string? NextProfileName);
