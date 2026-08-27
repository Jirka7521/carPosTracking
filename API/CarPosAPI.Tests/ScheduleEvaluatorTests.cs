using CarPosAPI.Services.Scheduling;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="ScheduleEvaluator"/> — the only genuinely tricky arithmetic in the
/// scheduling feature, and the one piece of it that can be tested without a database.
///
/// <para>
/// Every test is anchored to a real week: <b>Sunday 4 January 2026</b> is minute 0, so
/// a failure reads as "it thought Thursday lunchtime was the night profile" rather than
/// as two integers that disagree. The wrap cases — a window crossing midnight, and one
/// crossing the Saturday/Sunday seam that minute-of-week arithmetic folds — are the
/// reason this class exists.
/// </para>
/// </summary>
public sealed class ScheduleEvaluatorTests
{
    // Minute 0 of the evaluator's week. Verified: 1 January 2026 is a Thursday, so the
    // 4th is a Sunday.
    private static readonly DateTime SundayMidnight = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc);

    // Bit 0 is Sunday, matching DayOfWeek's numbering.
    private const int Weekdays = 0b0111110;   // Monday–Friday
    private const int SaturdayOnly = 0b1000000;
    private const int MondayOnly = 0b0000010;
    private const int EveryDay = 0b1111111;

    private static readonly Guid DayProfile = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NightProfile = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WeekendProfile = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FallbackProfile = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly ScheduleEvaluator _evaluator = new ScheduleEvaluator();

    [Fact]
    public void NoRules_ResolvesToTheFallbackAndNeverChanges()
    {
        ScheduleEvaluation result = _evaluator.Evaluate(
            Array.Empty<ScheduleRuleSnapshot>(),
            FallbackProfile,
            At(DayOfWeek.Thursday, 12, 0));

        Assert.Equal(FallbackProfile, result.ActiveProfileId);
        Assert.Null(result.ActiveRuleId);

        // Nothing can change it, so promising a switch would be a lie the countdown
        // beside it would then have to keep.
        Assert.Null(result.NextChangeAt);
        Assert.Null(result.ActiveSince);
    }

    [Fact]
    public void NoRulesAndNoFallback_ResolvesToNothing()
    {
        // Unreachable through the service, which refuses to enable a schedule without a
        // fallback — but the evaluator must still answer rather than assume.
        ScheduleEvaluation result = _evaluator.Evaluate(
            Array.Empty<ScheduleRuleSnapshot>(),
            fallbackProfileId: null,
            At(DayOfWeek.Thursday, 12, 0));

        Assert.Null(result.ActiveProfileId);
    }

    [Fact]
    public void WindowContainingTheInstant_WinsAndReportsBothItsEnds()
    {
        // Weekdays 06:00, sixteen hours long — so 06:00 to 22:00.
        ScheduleRuleSnapshot day = Rule(DayProfile, Weekdays, Minutes(6, 0), 16 * 60);

        ScheduleEvaluation result = _evaluator.Evaluate(
            new[] { day },
            FallbackProfile,
            At(DayOfWeek.Thursday, 12, 0));

        Assert.Equal(DayProfile, result.ActiveProfileId);
        Assert.Equal(day.RuleId, result.ActiveRuleId);
        Assert.Equal(At(DayOfWeek.Thursday, 6, 0), result.ActiveSince);
        Assert.Equal(At(DayOfWeek.Thursday, 22, 0), result.NextChangeAt);
        Assert.Equal(FallbackProfile, result.NextProfileId);
        Assert.Null(result.NextRuleId);
    }

    [Fact]
    public void WindowIsHalfOpen_SoAdjacentWindowsMeetWithoutAGapOrAnOverlap()
    {
        ScheduleRuleSnapshot day = Rule(DayProfile, Weekdays, Minutes(6, 0), 16 * 60);

        // The first minute of the window belongs to it...
        Assert.Equal(
            DayProfile,
            _evaluator.Evaluate(new[] { day }, FallbackProfile, At(DayOfWeek.Thursday, 6, 0)).ActiveProfileId);

        // ...and the minute the duration runs out does not.
        Assert.Equal(
            FallbackProfile,
            _evaluator.Evaluate(new[] { day }, FallbackProfile, At(DayOfWeek.Thursday, 22, 0)).ActiveProfileId);
    }

    [Fact]
    public void WindowCrossingMidnight_CoversTheFollowingMorning()
    {
        // Weekdays 22:00 for eight hours: it opens on Thursday and is still open at 02:00
        // on Friday — a different day, and the arithmetic must not need Friday to be one
        // of the days it opens on.
        ScheduleRuleSnapshot night = Rule(NightProfile, Weekdays, Minutes(22, 0), 8 * 60);

        ScheduleEvaluation result = _evaluator.Evaluate(
            new[] { night },
            FallbackProfile,
            At(DayOfWeek.Friday, 2, 0));

        Assert.Equal(NightProfile, result.ActiveProfileId);
        Assert.Equal(At(DayOfWeek.Thursday, 22, 0), result.ActiveSince);
        Assert.Equal(At(DayOfWeek.Friday, 6, 0), result.NextChangeAt);
    }

    [Fact]
    public void WindowCrossingTheWeekSeam_CoversSundayMorning()
    {
        // Saturday 22:00 for eight hours. In minute-of-week terms it starts at 9960 and
        // runs past 10080 back to 360 — the one case a naive "start <= m and m < end"
        // comparison gets wrong, and one a real weekend schedule hits every week.
        ScheduleRuleSnapshot weekendNight = Rule(WeekendProfile, SaturdayOnly, Minutes(22, 0), 8 * 60);

        ScheduleEvaluation result = _evaluator.Evaluate(
            new[] { weekendNight },
            FallbackProfile,
            At(DayOfWeek.Sunday, 2, 0));

        Assert.Equal(WeekendProfile, result.ActiveProfileId);

        // The window opened on the *previous* week's Saturday, so the instant is behind
        // the anchor rather than ahead of it.
        Assert.Equal(At(DayOfWeek.Saturday, 22, 0).AddDays(-7), result.ActiveSince);
        Assert.Equal(At(DayOfWeek.Sunday, 6, 0), result.NextChangeAt);
    }

    [Fact]
    public void OverlappingWindows_TheLowerPriorityNumberWins()
    {
        ScheduleRuleSnapshot everyDay = Rule(DayProfile, EveryDay, 0, 24 * 60, priority: 100);
        ScheduleRuleSnapshot weekend = Rule(WeekendProfile, SaturdayOnly, 0, 24 * 60, priority: 10);

        ScheduleEvaluation result = _evaluator.Evaluate(
            new[] { everyDay, weekend },
            FallbackProfile,
            At(DayOfWeek.Saturday, 12, 0));

        Assert.Equal(WeekendProfile, result.ActiveProfileId);
        Assert.Equal(weekend.RuleId, result.ActiveRuleId);

        // And it hands back to the all-week rule at midnight, not to the fallback.
        Assert.Equal(At(DayOfWeek.Saturday, 0, 0).AddDays(1), result.NextChangeAt);
        Assert.Equal(DayProfile, result.NextProfileId);
    }

    [Fact]
    public void EqualPriority_TheOlderRuleWins()
    {
        DateTime older = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime newer = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        ScheduleRuleSnapshot first = Rule(DayProfile, EveryDay, 0, 24 * 60, createdAt: older);
        ScheduleRuleSnapshot second = Rule(NightProfile, EveryDay, 0, 24 * 60, createdAt: newer);

        // Order in the list must not matter — that is the whole point of a stable
        // tie-break, so it is asserted from both directions.
        Assert.Equal(
            DayProfile,
            _evaluator.Evaluate(new[] { first, second }, null, At(DayOfWeek.Monday, 9, 0)).ActiveProfileId);
        Assert.Equal(
            DayProfile,
            _evaluator.Evaluate(new[] { second, first }, null, At(DayOfWeek.Monday, 9, 0)).ActiveProfileId);
    }

    [Fact]
    public void AdjacentWindowsNamingTheSameProfile_AreNotAChange()
    {
        // Two rules meeting at 14:00 that both select Day. The device would notice
        // nothing at 14:00, so announcing a switch there would be a countdown to an
        // event that never visibly happens.
        ScheduleRuleSnapshot morning = Rule(DayProfile, MondayOnly, Minutes(6, 0), 8 * 60);
        ScheduleRuleSnapshot afternoon = Rule(DayProfile, MondayOnly, Minutes(14, 0), 8 * 60);

        ScheduleEvaluation result = _evaluator.Evaluate(
            new[] { morning, afternoon },
            fallbackProfileId: null,
            At(DayOfWeek.Monday, 10, 0));

        Assert.Equal(DayProfile, result.ActiveProfileId);
        Assert.Equal(At(DayOfWeek.Monday, 6, 0), result.ActiveSince);
        Assert.Equal(At(DayOfWeek.Monday, 22, 0), result.NextChangeAt);
    }

    [Fact]
    public void ARuleCoveringTheWholeWeek_NeverChanges()
    {
        ScheduleRuleSnapshot always = Rule(DayProfile, EveryDay, 0, 24 * 60);

        ScheduleEvaluation result = _evaluator.Evaluate(
            new[] { always },
            FallbackProfile,
            At(DayOfWeek.Wednesday, 15, 30));

        Assert.Equal(DayProfile, result.ActiveProfileId);
        Assert.Null(result.NextChangeAt);

        // No boundary changes the answer, so there is no meaningful "since" either —
        // better null than an arbitrary midnight the reader would take for a fact.
        Assert.Null(result.ActiveSince);
    }

    [Fact]
    public void SecondsWithinTheMinuteAreIgnored()
    {
        ScheduleRuleSnapshot day = Rule(DayProfile, Weekdays, Minutes(6, 0), 16 * 60);

        // Windows are minute-granular, so a boundary must never come back as 22:00:37 —
        // an instant the rules do not contain, and one the countdown would render as an
        // odd number of seconds for ever.
        ScheduleEvaluation result = _evaluator.Evaluate(
            new[] { day },
            FallbackProfile,
            At(DayOfWeek.Thursday, 12, 0).AddSeconds(37));

        Assert.Equal(At(DayOfWeek.Thursday, 22, 0), result.NextChangeAt);
        Assert.Equal(At(DayOfWeek.Thursday, 6, 0), result.ActiveSince);
    }

    // -----------------------------------------------------------------------
    // Builders
    // -----------------------------------------------------------------------

    /// <summary>An instant in the anchor week.</summary>
    /// <param name="day">Which day of that week.</param>
    /// <param name="hour">Hour, UTC.</param>
    /// <param name="minute">Minute, UTC.</param>
    /// <returns>The UTC instant.</returns>
    private static DateTime At(DayOfWeek day, int hour, int minute)
    {
        return SundayMidnight.AddDays((int)day).AddHours(hour).AddMinutes(minute);
    }

    /// <summary>Minutes past midnight, spelled as a clock time.</summary>
    /// <param name="hour">Hour.</param>
    /// <param name="minute">Minute.</param>
    /// <returns>The minute of the day.</returns>
    private static int Minutes(int hour, int minute)
    {
        return (hour * 60) + minute;
    }

    /// <summary>Builds a rule with a fresh id and sensible defaults.</summary>
    /// <param name="profileId">The profile it selects.</param>
    /// <param name="daysMask">Which UTC days it opens on.</param>
    /// <param name="startMinute">Minutes past UTC midnight.</param>
    /// <param name="durationMinutes">How long it stays open.</param>
    /// <param name="priority">Lower wins.</param>
    /// <param name="createdAt">Tie-break for equal priorities.</param>
    /// <returns>The snapshot.</returns>
    private static ScheduleRuleSnapshot Rule(
        Guid profileId,
        int daysMask,
        int startMinute,
        int durationMinutes,
        int priority = 100,
        DateTime? createdAt = null)
    {
        return new ScheduleRuleSnapshot(
            Guid.NewGuid(),
            profileId,
            daysMask,
            startMinute,
            durationMinutes,
            priority,
            createdAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
