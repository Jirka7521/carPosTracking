using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Works out which profile a set of rules puts in force at a given instant, and when
/// that next changes.
///
/// <para>
/// <b>The whole feature's arithmetic lives here, and nothing else does.</b> No
/// database, no clock, no logging, no <see cref="DateTime.UtcNow"/> — the instant is a
/// parameter. That is what makes "a window that wraps past midnight on the Sunday of a
/// week that also wraps" something a test can state in three lines instead of something
/// discovered in production at 02:00.
/// </para>
///
/// <para>
/// <b>Everything is minute-of-week, UTC.</b> Minute 0 is Sunday 00:00, matching
/// <see cref="DayOfWeek"/>'s numbering so a day index is the enum value with no
/// translation table. A window is the half-open range
/// <c>[start, start + duration)</c> modulo the week, which is what lets two adjacent
/// windows meet at 06:00 with neither a one-minute gap nor a one-minute overlap.
/// </para>
///
/// <para>
/// <b>Boundaries, not scanning.</b> The active profile can only change where some
/// window opens or closes, so the search space is at most
/// <c>2 × 7 × MaxRulesPerDevice</c> instants rather than the 10 080 minutes of the
/// week. Both directions — the next change and the start of the current stretch — walk
/// that same small sorted set.
/// </para>
///
/// Stateless, and registered as a singleton for that reason.
/// </summary>
internal sealed class ScheduleEvaluator
{
    /// <summary>
    /// Resolves the schedule at <paramref name="utcNow"/>.
    /// </summary>
    /// <param name="rules">The device's enabled rules. Disabled ones must be filtered out by the caller.</param>
    /// <param name="fallbackProfileId">Applied wherever no window covers the instant; may be null.</param>
    /// <param name="utcNow">The instant to evaluate at. Seconds are ignored — windows are minute-granular.</param>
    /// <returns>The active profile, the next change, and the extent of the current stretch.</returns>
    public ScheduleEvaluation Evaluate(
        IReadOnlyList<ScheduleRuleSnapshot> rules,
        Guid? fallbackProfileId,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(rules);

        DateTime weekStartUtc = WeekStart(utcNow);
        int nowMinute = (int)(utcNow - weekStartUtc).TotalMinutes;

        ScheduleRuleSnapshot? activeRule = WinnerAt(rules, nowMinute);

        // Collected once and reused by both walks below. Sorted so "the next boundary
        // after m" is a scan from the right place rather than a re-sort per step.
        int[] boundaries = Boundaries(rules);

        DateTime? nextChangeAt = null;
        ScheduleRuleSnapshot? nextRule = null;
        DateTime? activeSince = null;

        if (boundaries.Length > 0)
        {
            // Forward: the first boundary at which the winner is a different profile.
            // Comparing profiles rather than rules is deliberate — two rules naming the
            // same profile back to back are not a change the device would notice, and
            // announcing one would be a lie the countdown then contradicts.
            for (int step = 0; step < boundaries.Length; step++)
            {
                int candidate = NextBoundaryAfter(boundaries, nowMinute, step);
                ScheduleRuleSnapshot? winner = WinnerAt(rules, candidate);

                if (ProfileOf(winner, fallbackProfileId) != ProfileOf(activeRule, fallbackProfileId))
                {
                    nextChangeAt = ToInstant(weekStartUtc, nowMinute, candidate, forward: true);
                    nextRule = winner;
                    break;
                }
            }

            // Backward: walk boundaries into the past until the winner was a different
            // profile; the stretch began at the boundary one step later than that.
            //
            // Step 0 is skipped because it cannot be the answer. The most recent
            // boundary at or before now opens the interval that *contains* now, so its
            // winner is by definition the active one — starting the loop at step 1 says
            // that as structure rather than relying on the reader to notice it.
            int stretchStart = PreviousBoundaryAtOrBefore(boundaries, nowMinute, 0);
            for (int step = 1; step < boundaries.Length; step++)
            {
                int candidate = PreviousBoundaryAtOrBefore(boundaries, nowMinute, step);
                ScheduleRuleSnapshot? winner = WinnerAt(rules, candidate);

                if (ProfileOf(winner, fallbackProfileId) != ProfileOf(activeRule, fallbackProfileId))
                {
                    break;
                }

                stretchStart = candidate;
            }

            // Left null when no boundary in the whole week changes the profile: a
            // schedule that always resolves the same way has no "since", and inventing
            // one from an arbitrary boundary would put a meaningless timestamp on screen.
            if (nextChangeAt is not null)
            {
                activeSince = ToInstant(weekStartUtc, nowMinute, stretchStart, forward: false);
            }
        }

        return new ScheduleEvaluation(
            ProfileOf(activeRule, fallbackProfileId),
            activeRule?.RuleId,
            activeSince,
            nextChangeAt,
            ProfileOf(nextRule, fallbackProfileId),
            nextRule?.RuleId);
    }

    /// <summary>
    /// The rule whose window covers <paramref name="minuteOfWeek"/> and beats every
    /// other that does.
    /// </summary>
    /// <param name="rules">The enabled rules.</param>
    /// <param name="minuteOfWeek">Minute of the UTC week, 0–10079.</param>
    /// <returns>The winning rule, or null when no window covers that minute.</returns>
    private static ScheduleRuleSnapshot? WinnerAt(
        IReadOnlyList<ScheduleRuleSnapshot> rules,
        int minuteOfWeek)
    {
        ScheduleRuleSnapshot? best = null;

        foreach (ScheduleRuleSnapshot rule in rules)
        {
            if (!Covers(rule, minuteOfWeek))
            {
                continue;
            }

            if (best is null || Beats(rule, best))
            {
                best = rule;
            }
        }

        return best;
    }

    /// <summary>Whether a rule's window contains a minute of the week.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="minuteOfWeek">Minute of the UTC week, 0–10079.</param>
    /// <returns>True when covered.</returns>
    private static bool Covers(ScheduleRuleSnapshot rule, int minuteOfWeek)
    {
        for (int day = 0; day < 7; day++)
        {
            if ((rule.DaysMaskUtc & (1 << day)) == 0)
            {
                continue;
            }

            int windowStart = (day * ScheduleRules.MinutesPerDay) + rule.StartMinuteUtc;

            // Modulo the week, so a Saturday-evening window that runs into Sunday
            // morning is one window rather than two — including across minute 0, which
            // is the case a naive "start <= m && m < end" comparison gets wrong.
            int offset = Modulo(minuteOfWeek - windowStart, ScheduleRules.MinutesPerWeek);
            if (offset < rule.DurationMinutes)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="candidate"/> outranks <paramref name="incumbent"/>.</summary>
    /// <param name="candidate">The rule being considered.</param>
    /// <param name="incumbent">The best rule so far.</param>
    /// <returns>True when the candidate should win.</returns>
    private static bool Beats(ScheduleRuleSnapshot candidate, ScheduleRuleSnapshot incumbent)
    {
        if (candidate.Priority != incumbent.Priority)
        {
            return candidate.Priority < incumbent.Priority;
        }

        if (candidate.CreatedAt != incumbent.CreatedAt)
        {
            // Older wins. Arbitrary, but it must be *something* stable, or a schedule
            // with two same-priority rules would resolve by row order.
            return candidate.CreatedAt < incumbent.CreatedAt;
        }

        // Two rules created in the same tick. Vanishingly unlikely, but "vanishingly
        // unlikely" is not "deterministic", and the id is always there to fall back on.
        return candidate.RuleId.CompareTo(incumbent.RuleId) < 0;
    }

    /// <summary>
    /// Every minute of the week at which some window opens or closes, sorted and
    /// de-duplicated.
    /// </summary>
    /// <param name="rules">The enabled rules.</param>
    /// <returns>The candidate change points; empty when there are no rules.</returns>
    private static int[] Boundaries(IReadOnlyList<ScheduleRuleSnapshot> rules)
    {
        HashSet<int> points = new HashSet<int>();

        foreach (ScheduleRuleSnapshot rule in rules)
        {
            for (int day = 0; day < 7; day++)
            {
                if ((rule.DaysMaskUtc & (1 << day)) == 0)
                {
                    continue;
                }

                int windowStart = (day * ScheduleRules.MinutesPerDay) + rule.StartMinuteUtc;
                points.Add(Modulo(windowStart, ScheduleRules.MinutesPerWeek));
                points.Add(Modulo(windowStart + rule.DurationMinutes, ScheduleRules.MinutesPerWeek));
            }
        }

        int[] sorted = points.ToArray();
        Array.Sort(sorted);
        return sorted;
    }

    /// <summary>
    /// The <paramref name="step"/>-th boundary strictly after <paramref name="fromMinute"/>,
    /// wrapping around the week.
    /// </summary>
    /// <param name="boundaries">Sorted, de-duplicated boundary minutes.</param>
    /// <param name="fromMinute">Where to start looking.</param>
    /// <param name="step">0 for the next one, 1 for the one after, and so on.</param>
    /// <returns>A minute of the week.</returns>
    private static int NextBoundaryAfter(int[] boundaries, int fromMinute, int step)
    {
        int firstAfter = 0;
        while (firstAfter < boundaries.Length && boundaries[firstAfter] <= fromMinute)
        {
            firstAfter++;
        }

        return boundaries[(firstAfter + step) % boundaries.Length];
    }

    /// <summary>
    /// The <paramref name="step"/>-th boundary at or before <paramref name="fromMinute"/>,
    /// walking backwards and wrapping around the week.
    /// </summary>
    /// <param name="boundaries">Sorted, de-duplicated boundary minutes.</param>
    /// <param name="fromMinute">Where to start looking.</param>
    /// <param name="step">0 for the most recent one, 1 for the one before it, and so on.</param>
    /// <returns>A minute of the week.</returns>
    private static int PreviousBoundaryAtOrBefore(int[] boundaries, int fromMinute, int step)
    {
        int lastAtOrBefore = boundaries.Length - 1;
        while (lastAtOrBefore >= 0 && boundaries[lastAtOrBefore] > fromMinute)
        {
            lastAtOrBefore--;
        }

        // No boundary at or before it means the most recent one is last week's final
        // boundary — hence the wrap rather than a clamp.
        int index = Modulo(lastAtOrBefore - step, boundaries.Length);
        return boundaries[index];
    }

    /// <summary>
    /// Turns a minute of the week back into an absolute instant, in the direction the
    /// caller is walking.
    /// </summary>
    /// <param name="weekStartUtc">Sunday 00:00 UTC of the week containing "now".</param>
    /// <param name="nowMinute">The current minute of that week.</param>
    /// <param name="targetMinute">The minute of the week being converted.</param>
    /// <param name="forward">True for an instant after now, false for one at or before it.</param>
    /// <returns>The instant, as UTC.</returns>
    private static DateTime ToInstant(DateTime weekStartUtc, int nowMinute, int targetMinute, bool forward)
    {
        int deltaMinutes = targetMinute - nowMinute;

        if (forward && deltaMinutes <= 0)
        {
            // The boundary is earlier in the week's numbering than we are, so the next
            // occurrence of it is in the week ahead.
            deltaMinutes += ScheduleRules.MinutesPerWeek;
        }
        else if (!forward && deltaMinutes > 0)
        {
            deltaMinutes -= ScheduleRules.MinutesPerWeek;
        }

        // Truncated to the minute, matching the granularity the windows are defined at:
        // a boundary reported as 22:00:37 would be a boundary the rules do not have.
        DateTime nowAtMinute = weekStartUtc.AddMinutes(nowMinute);
        return nowAtMinute.AddMinutes(deltaMinutes);
    }

    /// <summary>Sunday 00:00 UTC of the week containing an instant.</summary>
    /// <param name="utcNow">The instant.</param>
    /// <returns>The week's origin, with <see cref="DateTimeKind.Utc"/>.</returns>
    private static DateTime WeekStart(DateTime utcNow)
    {
        // .Date drops the kind on some paths, and Npgsql throws on a timestamptz
        // parameter whose kind is not Utc — so it is restated rather than assumed.
        DateTime midnight = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
        return midnight.AddDays(-(int)utcNow.DayOfWeek);
    }

    /// <summary>
    /// A modulo that returns a non-negative result, which C#'s <c>%</c> does not for a
    /// negative left operand — and every wrap in this file has one.
    /// </summary>
    /// <param name="value">The dividend, possibly negative.</param>
    /// <param name="modulus">The divisor; must be positive.</param>
    /// <returns>A value in <c>[0, modulus)</c>.</returns>
    private static int Modulo(int value, int modulus)
    {
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }

    /// <summary>The profile a winning rule selects, or the fallback when nothing won.</summary>
    /// <param name="rule">The winning rule, or null.</param>
    /// <param name="fallbackProfileId">The schedule's fallback.</param>
    /// <returns>The profile id, or null when there is neither.</returns>
    private static Guid? ProfileOf(ScheduleRuleSnapshot? rule, Guid? fallbackProfileId)
    {
        return rule?.ProfileId ?? fallbackProfileId;
    }
}
