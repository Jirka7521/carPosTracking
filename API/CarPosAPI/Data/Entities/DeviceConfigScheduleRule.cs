namespace CarPosAPI.Data.Entities;

/// <summary>
/// One weekly time window that selects a <see cref="DeviceConfigProfile"/>.
///
/// <para>
/// <b>Everything here is UTC.</b> The API never sees a local time: the dashboard
/// converts what the reader types into these fields and back again, which is why
/// there is no timezone column. The consequence, deliberately accepted, is that a
/// window entered as "22:00" in winter renders as 23:00 after the spring DST change
/// — the stored instant did not move, the local clock did. The UI shows both times
/// on every rule so this is visible rather than mysterious.
/// </para>
///
/// <para>
/// The window is a start plus a length rather than a start and an end. An end time
/// would need a "smaller than the start means it wraps past midnight" convention,
/// which every reader and every query would then have to remember; a length is
/// unambiguous, makes the CHECK constraints trivial, and survives conversion between
/// timezones untouched, because a duration has no offset.
/// </para>
///
/// <para>
/// Evaluated by <see cref="Services.Scheduling.ScheduleEvaluator"/>. Mapped by
/// <see cref="Configurations.DeviceConfigScheduleRuleConfiguration"/>.
/// </para>
/// </summary>
public sealed class DeviceConfigScheduleRule
{
    /// <summary>Internal primary key. DB-generated.</summary>
    public Guid Id { get; set; }

    /// <summary>The device this rule belongs to (FK to <c>devices.id</c>).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>The profile this window puts in force (FK to <c>device_config_profiles.id</c>).</summary>
    public Guid ProfileId { get; set; }

    /// <summary>
    /// Which <em>UTC</em> weekdays a window may begin on, as a 7-bit mask: bit 0 is
    /// Sunday, matching <see cref="DayOfWeek"/>'s numbering so the evaluator can shift
    /// by the enum value directly. At least one bit must be set — a rule that can
    /// never start is a rule nobody meant to write.
    /// </summary>
    public int DaysMaskUtc { get; set; }

    /// <summary>Minutes past UTC midnight at which the window opens; 0–1439.</summary>
    public int StartMinuteUtc { get; set; }

    /// <summary>
    /// How long the window stays open, in minutes; 1–1440. A window may run past
    /// midnight into the next UTC day, and the last minute is exclusive — so 1440
    /// means the whole day with no gap and no overlap at the seam.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Tie-break where windows overlap: <b>lower wins</b>. Ordering is by priority
    /// then by <see cref="CreatedAt"/>, so two rules left at the same priority still
    /// resolve deterministically instead of depending on row order.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether the evaluator considers this rule at all. Lets a seasonal window be
    /// parked for a few months without losing the times somebody worked out.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When the rule was created (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }
}
