namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// What a schedule resolves to at one instant, and what happens next.
///
/// <para>
/// Everything the dashboard needs to explain itself comes from one call: which profile
/// is in force, which rule chose it (or none, meaning the fallback), how long it has
/// been in force, and when and what it changes to. That is the whole point of
/// evaluating windows rather than firing timers — a schedule built from one-shot
/// triggers could say what it did last, but never what is true now.
/// </para>
/// </summary>
/// <param name="ActiveProfileId">
/// The profile in force. Null only when no rule matches and no fallback is set, which
/// the service refuses to let an enabled schedule reach.
/// </param>
/// <param name="ActiveRuleId">The rule whose window contains the instant, or null when the fallback won.</param>
/// <param name="ActiveSince">
/// When the current stretch began (UTC), or null when the schedule never changes — a
/// single rule covering the whole week has been in force since before anyone asked.
/// </param>
/// <param name="NextChangeAt">
/// When the active profile next changes (UTC), or null when it never does. Also the
/// instant a manual override is set to expire at.
/// </param>
/// <param name="NextProfileId">The profile taking over at <paramref name="NextChangeAt"/>.</param>
/// <param name="NextRuleId">The rule taking over, or null when the fallback does.</param>
internal sealed record ScheduleEvaluation(
    Guid? ActiveProfileId,
    Guid? ActiveRuleId,
    DateTime? ActiveSince,
    DateTime? NextChangeAt,
    Guid? NextProfileId,
    Guid? NextRuleId);
