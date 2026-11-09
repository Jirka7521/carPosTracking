using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// The stop-finding primitive shared by <see cref="TripDetector"/> (which stores trip
/// starts) and <see cref="TripSegmentAnalyzer"/> (which rebuilds each trip's end and
/// statistics on read). Because both walk fixes with exactly these functions, the end a
/// read finds is the end the detector used — that agreement is what lets the database
/// store nothing but starts.
///
/// <para>
/// <b>Stops are places, not gaps.</b> A parked car keeps reporting (15 min awake, then a
/// fix every 20 min), so a stop is a run of fixes that stay within the stop radius of
/// its first fix — the anchor — for at least the minimum stop. Two rules handle silence:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Sleep:</b> if the car had already stood still for
/// <see cref="TripRules.StillBeforeSleepSeconds"/> before a data gap, the gap counts as
/// parked time — the firmware only sleeps after standing still. This is what ends a trip
/// correctly when the car stood 25 min and the wake fix was already 500 m away.
/// </description></item>
/// <item><description>
/// <b>Hard gap:</b> a silence of <see cref="TripRules.MaxBridgedGapSeconds"/> or more
/// across which the car moved further than the radius ends the trip; bridging it with a
/// straight line would invent a drive.
/// </description></item>
/// </list>
///
/// <para>
/// <b>Memoryless by construction.</b> Every answer depends only on the fixes from the
/// probed index forward. That is what makes a scan restarted at a stop's first fix
/// reproduce the original scan exactly (the restart property the worker relies on).
/// </para>
///
/// <para>
/// <b>Truncated input.</b> Callers that load fixes in chunks pass
/// <c>moreDataAvailable</c>; when an answer would depend on fixes beyond the list, the
/// functions say "undetermined" (null) instead of guessing.
/// </para>
/// </summary>
internal static class StopFinder
{
    /// <summary>Decides whether the fix at <paramref name="index"/> is the first fix of a stop.</summary>
    /// <param name="points">Fixes in ascending time order.</param>
    /// <param name="index">The candidate anchor.</param>
    /// <param name="settings">Thresholds.</param>
    /// <param name="nowUtc">
    /// The current time, used only when the fixes end inside a still spell and no more
    /// exist: the silence since then is the device asleep, parked. Null when a later fix
    /// is known to exist (or when the caller must not depend on the clock).
    /// </param>
    /// <param name="moreDataAvailable">True when fixes exist beyond the end of <paramref name="points"/>.</param>
    /// <param name="needExtent">
    /// True to scan the whole stop even after it is confirmed (the detector needs to jump
    /// past it); false to answer as soon as the stop is confirmed.
    /// </param>
    /// <returns>The probe outcome.</returns>
    public static StopProbe ProbeStop(
        IReadOnlyList<TripPoint> points,
        int index,
        TripDetectionSettings settings,
        DateTime? nowUtc,
        bool moreDataAvailable,
        bool needExtent)
    {
        TripPoint anchor = points[index];
        bool confirmed = false;
        int next = index + 1;

        while (next < points.Count && GeoDistance.Meters(anchor, points[next]) <= settings.StopRadiusMeters)
        {
            if (!confirmed && (points[next].FixTime - anchor.FixTime).TotalSeconds >= settings.MinStopSeconds)
            {
                confirmed = true;
                if (!needExtent)
                {
                    return new StopProbe(true, next, false);
                }
            }

            next++;
        }

        int lastIndex = next - 1;
        bool reachedEnd = next == points.Count;

        if (confirmed)
        {
            return new StopProbe(true, lastIndex, reachedEnd);
        }

        // The still spell runs into the end of what we were given and more fixes exist:
        // they may extend it into a stop, so nothing can be said yet.
        if (reachedEnd && moreDataAvailable)
        {
            return new StopProbe(null, lastIndex, reachedEnd);
        }

        DateTime lastStillTime = points[lastIndex].FixTime;
        double stillSeconds = (lastStillTime - anchor.FixTime).TotalSeconds;
        DateTime spanEnd = lastStillTime;

        // The sleep rule: silence after a still spell is parked time. It ends at the next
        // fix, or — for the newest fixes of all — now.
        DateTime? silenceEnd = reachedEnd ? nowUtc : points[next].FixTime;
        if (silenceEnd is DateTime silenceEndUtc
            && stillSeconds >= TripRules.StillBeforeSleepSeconds
            && (silenceEndUtc - lastStillTime).TotalSeconds >= TripRules.DataGapSeconds)
        {
            spanEnd = silenceEndUtc;
        }

        bool isStop = (spanEnd - anchor.FixTime).TotalSeconds >= settings.MinStopSeconds;
        return new StopProbe(isStop, lastIndex, reachedEnd);
    }

    /// <summary>
    /// True when the silence between two consecutive fixes is long and the car moved
    /// across it — the trip cannot honestly continue through it.
    /// </summary>
    /// <param name="from">The fix before the silence.</param>
    /// <param name="to">The fix after it.</param>
    /// <param name="settings">Thresholds.</param>
    /// <returns>True for a hard gap.</returns>
    public static bool IsHardGap(TripPoint from, TripPoint to, TripDetectionSettings settings) =>
        (to.FixTime - from.FixTime).TotalSeconds >= TripRules.MaxBridgedGapSeconds
        && GeoDistance.Meters(from, to) > settings.StopRadiusMeters;

    /// <summary>
    /// The fix that leads into a trip — the last fix of the preceding stop — or null when
    /// there is none or a hard gap separates it from the start.
    /// </summary>
    /// <param name="previous">The fix immediately before the trip's start, or null.</param>
    /// <param name="start">The trip's first fix.</param>
    /// <param name="settings">Thresholds.</param>
    /// <returns><paramref name="previous"/>, or null when it must not count.</returns>
    public static TripPoint? LeadIn(TripPoint? previous, TripPoint start, TripDetectionSettings settings) =>
        previous is not null && !IsHardGap(previous, start, settings) ? previous : null;

    /// <summary>
    /// Walks forward from a trip's first fix to where the trip ends: the first stop, the
    /// first hard gap, or the end of the fixes.
    /// </summary>
    /// <param name="points">Fixes in ascending time order.</param>
    /// <param name="startIndex">The trip's first fix — known not to be a stop anchor itself.</param>
    /// <param name="settings">Thresholds.</param>
    /// <param name="nowUtc">See <see cref="ProbeStop"/>.</param>
    /// <param name="moreDataAvailable">True when fixes exist beyond the end of <paramref name="points"/>.</param>
    /// <returns>The end, or null when it lies beyond the fixes given (load more and retry).</returns>
    public static TripSegmentEnd? FindEnd(
        IReadOnlyList<TripPoint> points,
        int startIndex,
        TripDetectionSettings settings,
        DateTime? nowUtc,
        bool moreDataAvailable)
    {
        for (int index = startIndex; index < points.Count; index++)
        {
            // The start itself is never probed: whoever called us already knows it is a
            // moving fix, and probing it again (possibly with a clock) could disagree.
            if (index > startIndex)
            {
                StopProbe probe = ProbeStop(points, index, settings, nowUtc, moreDataAvailable, needExtent: false);
                if (probe.IsStop is null)
                {
                    return null;
                }

                if (probe.IsStop.Value)
                {
                    return new TripSegmentEnd(index, TripEndKind.Stay);
                }
            }

            if (index + 1 < points.Count)
            {
                if (IsHardGap(points[index], points[index + 1], settings))
                {
                    return new TripSegmentEnd(index, TripEndKind.Gap);
                }
            }
            else if (moreDataAvailable)
            {
                return null;
            }
        }

        return new TripSegmentEnd(points.Count - 1, TripEndKind.Open);
    }
}
