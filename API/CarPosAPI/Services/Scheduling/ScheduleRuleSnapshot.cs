namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// One rule as <see cref="ScheduleEvaluator"/> needs it: the window, who wins a tie,
/// and which profile it selects.
///
/// <para>
/// A separate type from <see cref="Data.Entities.DeviceConfigScheduleRule"/> so the
/// evaluator has no dependency on EF Core, no navigation properties to accidentally
/// lazy-load, and nothing in it that a test would have to construct a DbContext to
/// produce. It is the reason the evaluator — the only genuinely tricky arithmetic in
/// this feature — can be unit-tested with plain records in a project that has no
/// database fixture.
/// </para>
/// </summary>
/// <param name="RuleId">Identifies the rule in the result, so the UI can say which one matched.</param>
/// <param name="ProfileId">The profile this window puts in force.</param>
/// <param name="DaysMaskUtc">7-bit mask of UTC weekdays the window may open on; bit 0 is Sunday.</param>
/// <param name="StartMinuteUtc">Minutes past UTC midnight at which it opens; 0–1439.</param>
/// <param name="DurationMinutes">How long it stays open; 1–1440. The end minute is exclusive.</param>
/// <param name="Priority">Lower wins where windows overlap.</param>
/// <param name="CreatedAt">
/// Breaks a tie between two rules left at the same priority. Without it the winner
/// would depend on row order, and a schedule could resolve differently after a
/// database restore — the same rules, a different tracker.
/// </param>
internal sealed record ScheduleRuleSnapshot(
    Guid RuleId,
    Guid ProfileId,
    int DaysMaskUtc,
    int StartMinuteUtc,
    int DurationMinutes,
    int Priority,
    DateTime CreatedAt);
