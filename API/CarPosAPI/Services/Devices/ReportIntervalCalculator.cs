namespace CarPosAPI.Services.Devices;

/// <summary>
/// Works out the longest a device may legitimately stay quiet between two reports, and
/// from that, how old its latest fix may get before the map calls it overdue.
///
/// <para>
/// <b>The worst case, on purpose.</b> A red pin claims "this tracker should have
/// reported by now", so every set of settings that might be in force is considered and
/// the longest interval wins. A pin that turns red a little late is a smaller harm than
/// one that cries wolf every night.
/// </para>
///
/// <para>
/// <b>Which sets count.</b> While a schedule is driving the device (enabled, no live
/// override), the profile active now and the profile the device last reported — the
/// one its latest fix was taken under. Taking both stops a false alarm right after a
/// switch from a long-interval plan to a short one: the device is still asleep on the
/// old plan's timer. Otherwise, the desired and applied config revisions, because a
/// sleeping device keeps running the applied one until it wakes and picks up the new.
/// </para>
///
/// <para>
/// <b>Within a set, standby is the long one.</b> A parked car reports least often, so
/// the interval of a set is its standby interval — or the moving interval in the odd
/// configuration where that is longer and motion wake is on.
/// </para>
///
/// No database and no clock, so it is a stateless singleton and testable with plain
/// records.
/// </summary>
internal sealed class ReportIntervalCalculator
{
    /// <summary>
    /// How many worst-case intervals may pass before the latest fix counts as overdue.
    /// Two, so one lost report (no fix in a garage, a dropped upload) is not an alarm.
    /// The dashboard never sees this factor, only its product.
    /// </summary>
    public const int OverdueIntervalFactor = 2;

    /// <summary>The longest reporting interval any set that may be in force allows.</summary>
    /// <param name="scheduleInForce">True while a schedule is enabled and no override is live.</param>
    /// <param name="activeProfile">The profile the schedule selects now, or null.</param>
    /// <param name="reportedProfile">The profile the device last reported running, or null.</param>
    /// <param name="desired">The config revision published to the device, or null.</param>
    /// <param name="applied">The config revision the device last confirmed, or null.</param>
    /// <returns>Seconds, or null when no set is known at all.</returns>
    public int? MaxIntervalSeconds(
        bool scheduleInForce,
        ReportIntervalCandidate? activeProfile,
        ReportIntervalCandidate? reportedProfile,
        ReportIntervalCandidate? desired,
        ReportIntervalCandidate? applied)
    {
        if (scheduleInForce)
        {
            int? fromSchedule = Longest(activeProfile, reportedProfile);

            // A schedule that resolves to nothing leaves the device on its config
            // document, so that is what it falls back to here as well.
            if (fromSchedule is not null)
            {
                return fromSchedule;
            }
        }

        return Longest(desired, applied);
    }

    /// <summary>How old the latest fix may get before it is overdue.</summary>
    /// <param name="maxIntervalSeconds">The worst-case interval, or null.</param>
    /// <returns>Seconds, or null when the interval is unknown.</returns>
    public int? OverdueAfterSeconds(int? maxIntervalSeconds)
    {
        return maxIntervalSeconds * OverdueIntervalFactor;
    }

    /// <summary>The longest interval among two sets, either of which may be missing.</summary>
    /// <param name="first">One set, or null.</param>
    /// <param name="second">The other, or null.</param>
    /// <returns>Seconds, or null when both are missing.</returns>
    private static int? Longest(ReportIntervalCandidate? first, ReportIntervalCandidate? second)
    {
        if (first is null)
        {
            return second is null ? null : IntervalOf(second);
        }

        return second is null
            ? IntervalOf(first)
            : Math.Max(IntervalOf(first), IntervalOf(second));
    }

    /// <summary>The longest interval one set of values allows.</summary>
    /// <param name="candidate">The set.</param>
    /// <returns>Seconds.</returns>
    private static int IntervalOf(ReportIntervalCandidate candidate)
    {
        return candidate.MotionEnabled
            ? Math.Max(candidate.IntervalSeconds, candidate.MovingIntervalSeconds)
            : candidate.IntervalSeconds;
    }
}
