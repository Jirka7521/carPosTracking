using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Rebuilds one trip from its stored start: where it ended and what it measured
/// (distance, duration, moving time, speeds). This runs on every read — nothing it
/// returns is stored — and <see cref="TripDetector"/> uses <see cref="Measure"/> too, so
/// the minimum-distance rule judges exactly the distance the dashboard later shows.
///
/// <para>
/// No database, no clock: "now" is a parameter, like <c>ScheduleEvaluator</c>.
/// </para>
/// </summary>
internal static class TripSegmentAnalyzer
{
    private const double MetersPerSecondToKmph = 3.6;

    /// <summary>Finds a trip's end and measures it.</summary>
    /// <param name="previousFix">The fix immediately before the trip's start, or null if there is none.</param>
    /// <param name="points">
    /// The trip's fixes, starting with its first. For a trip with a known successor, up to
    /// and including the successor's first fix — the stop between them ends there.
    /// </param>
    /// <param name="settings">Thresholds.</param>
    /// <param name="nowUtc">
    /// The current time for the newest trip (decides "in progress" and lets a still spell
    /// at the very end count as a stop); null when a successor follows.
    /// </param>
    /// <param name="moreDataAvailable">True when fixes exist beyond <paramref name="points"/> that were not loaded.</param>
    /// <param name="lastPointStartsNextTrip">
    /// True when the last element of <paramref name="points"/> is the successor's first fix.
    /// </param>
    /// <returns>The trip's statistics, or null when more fixes must be loaded to decide its end.</returns>
    public static TripSegmentStats? Analyze(
        TripPoint? previousFix,
        IReadOnlyList<TripPoint> points,
        TripDetectionSettings settings,
        DateTime? nowUtc,
        bool moreDataAvailable,
        bool lastPointStartsNextTrip)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(settings);
        if (points.Count == 0)
        {
            throw new ArgumentException("A trip has at least its first fix.", nameof(points));
        }

        TripSegmentEnd? end = StopFinder.FindEnd(points, 0, settings, nowUtc, moreDataAvailable);
        if (end is null)
        {
            return null;
        }

        // With consistent data the scan stops at the stop before the successor. Reaching
        // the successor means the stored starts predate the data or the thresholds (a
        // recompute is pending): end just before it rather than swallow the next trip.
        if (lastPointStartsNextTrip && end.Kind == TripEndKind.Open && points.Count > 1)
        {
            end = new TripSegmentEnd(points.Count - 2, TripEndKind.Stay);
        }

        TripPoint? leadIn = StopFinder.LeadIn(previousFix, points[0], settings);
        return Measure(leadIn, points, 0, end.EndIndex, end.Kind, settings, nowUtc);
    }

    /// <summary>Measures the fixes from <paramref name="startIndex"/> to <paramref name="endIndex"/>.</summary>
    /// <param name="leadIn">The last fix of the preceding stop, or null (see <see cref="StopFinder.LeadIn"/>).</param>
    /// <param name="points">Fixes in ascending time order.</param>
    /// <param name="startIndex">The trip's first fix.</param>
    /// <param name="endIndex">The trip's last fix (inclusive).</param>
    /// <param name="endKind">How the trip ended.</param>
    /// <param name="settings">Thresholds.</param>
    /// <param name="nowUtc">The current time, or null; only used for <see cref="TripSegmentStats.InProgress"/>.</param>
    /// <returns>The statistics.</returns>
    public static TripSegmentStats Measure(
        TripPoint? leadIn,
        IReadOnlyList<TripPoint> points,
        int startIndex,
        int endIndex,
        TripEndKind endKind,
        TripDetectionSettings settings,
        DateTime? nowUtc)
    {
        TripPoint first = points[startIndex];
        DateTime departure = first.FixTime;
        double distanceMeters = 0;
        double movingSeconds = 0;
        double maxSpeedKmph = first.SpeedKmph;
        bool hasGap = false;

        if (leadIn is not null)
        {
            // The leg out of the parking spot is real driving, so it always counts as
            // distance. It sets the departure only when the car left within a data gap of
            // the stop's last fix; after a sleep, when it left is unknown and the first
            // moving fix is the honest start.
            double leadInMeters = GeoDistance.Meters(leadIn, first);
            double leadInSeconds = (first.FixTime - leadIn.FixTime).TotalSeconds;
            distanceMeters += leadInMeters;
            if (leadInSeconds < TripRules.DataGapSeconds)
            {
                departure = leadIn.FixTime;
                movingSeconds += MovingSecondsOf(leadInMeters, leadInSeconds, first.SpeedKmph);
            }
        }

        for (int index = startIndex + 1; index <= endIndex; index++)
        {
            TripPoint from = points[index - 1];
            TripPoint to = points[index];
            double legMeters = GeoDistance.Meters(from, to);
            double legSeconds = (to.FixTime - from.FixTime).TotalSeconds;

            distanceMeters += legMeters;
            maxSpeedKmph = Math.Max(maxSpeedKmph, to.SpeedKmph);

            if (legSeconds >= TripRules.DataGapSeconds)
            {
                // Silence inside a trip: never moving time (we don't know), and a gap the
                // dashboard warns about if the car covered ground during it.
                if (legMeters > settings.StopRadiusMeters)
                {
                    hasGap = true;
                }
            }
            else
            {
                movingSeconds += MovingSecondsOf(legMeters, legSeconds, to.SpeedKmph);
            }
        }

        DateTime end = points[endIndex].FixTime;
        bool inProgress = endKind == TripEndKind.Open
            && nowUtc is DateTime now
            && (now - end).TotalSeconds < settings.MinStopSeconds;

        return new TripSegmentStats(
            departure,
            end,
            endKind,
            inProgress,
            distanceMeters,
            (end - departure).TotalSeconds,
            movingSeconds,
            maxSpeedKmph,
            hasGap,
            endIndex - startIndex + 1);
    }

    // A short leg counts as moving when either its average speed or the receiver's speed at
    // its end is above the threshold — the receiver catches a crawl the 30 s average hides,
    // the average catches a fix whose instantaneous speed was momentarily zero.
    private static double MovingSecondsOf(double legMeters, double legSeconds, double endSpeedKmph)
    {
        if (legSeconds <= 0)
        {
            return 0;
        }

        double legSpeedKmph = legMeters / legSeconds * MetersPerSecondToKmph;
        return Math.Max(legSpeedKmph, endSpeedKmph) > TripRules.MovingSpeedKmph ? legSeconds : 0;
    }
}
