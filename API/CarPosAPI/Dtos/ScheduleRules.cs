namespace CarPosAPI.Dtos;

/// <summary>
/// The bounds on schedules — profile names, window arithmetic, and how many of each
/// a device may have.
///
/// <para>
/// The sibling of <see cref="DeviceConfigRules"/> and here for the same reason: these
/// constants feed the <c>[Range]</c> attributes on the request DTOs, the CHECK
/// constraints in <c>DeviceConfigProfileConfiguration</c> and
/// <c>DeviceConfigScheduleRuleConfiguration</c>, and the <c>min</c>/<c>max</c> on the
/// dashboard's inputs. One definition, three enforcement points.
/// </para>
///
/// <para>
/// Unlike <see cref="DeviceConfigRules"/>, <b>none of this is mirrored in the
/// firmware</b> — the device never learns a schedule exists. It receives the same
/// configuration document it always did, and cannot tell a revision the scheduler
/// wrote from one a person saved. That is what allowed schedules to be built without
/// touching <c>ESP32/</c> at all.
/// </para>
/// </summary>
public static class ScheduleRules
{
    /// <summary>Shortest acceptable profile name.</summary>
    public const int MinProfileNameLength = 1;

    /// <summary>
    /// Longest acceptable profile name. Matched to the device alias limit in
    /// <c>DeviceAliasUpdateRequestDto</c> — both are short labels that must fit a
    /// list row without wrapping.
    /// </summary>
    public const int MaxProfileNameLength = 40;

    /// <summary>
    /// How many profiles one device may have. A policy bound, not a technical one:
    /// a schedule nobody can hold in their head is not a feature, and every profile
    /// past a handful is a value set somebody will forget they wrote.
    /// </summary>
    public const int MaxProfilesPerDevice = 12;

    /// <summary>
    /// How many rules one device may have. Generous enough for a different window
    /// every few hours on every day of the week; small enough that the evaluator's
    /// per-tick work stays trivially bounded.
    /// </summary>
    public const int MaxRulesPerDevice = 32;

    /// <summary>
    /// Smallest legal weekday mask. Zero is excluded on purpose — a rule whose
    /// window can never open is not a disabled rule, it is a mistake, and
    /// <see cref="Data.Entities.DeviceConfigScheduleRule.IsEnabled"/> is the honest
    /// way to say "not for now".
    /// </summary>
    public const int MinDaysMask = 1;

    /// <summary>All seven days set — the mask is 7 bits, Sunday in bit 0.</summary>
    public const int MaxDaysMask = 127;

    /// <summary>Number of minutes in a day, and so the exclusive ceiling on a start minute.</summary>
    public const int MinutesPerDay = 1440;

    /// <summary>Number of minutes in a week — the span the evaluator works in.</summary>
    public const int MinutesPerWeek = MinutesPerDay * 7;

    /// <summary>Earliest a window may open: UTC midnight.</summary>
    public const int MinStartMinute = 0;

    /// <summary>Latest a window may open: 23:59 UTC.</summary>
    public const int MaxStartMinute = MinutesPerDay - 1;

    /// <summary>Shortest window. A one-minute window is odd but not wrong, and forbidding it buys nothing.</summary>
    public const int MinDurationMinutes = 1;

    /// <summary>
    /// Longest window: a full day. Longer would let two consecutive starts of the
    /// same rule overlap each other, which has no meaning — "always" is better said
    /// with a full-week mask, or by making the profile the fallback.
    /// </summary>
    public const int MaxDurationMinutes = MinutesPerDay;

    /// <summary>Highest-precedence rule priority (lower wins).</summary>
    public const int MinPriority = 0;

    /// <summary>Lowest-precedence rule priority.</summary>
    public const int MaxPriority = 1000;

    /// <summary>Priority given to a rule whose author did not choose one.</summary>
    public const int DefaultPriority = 100;
}
