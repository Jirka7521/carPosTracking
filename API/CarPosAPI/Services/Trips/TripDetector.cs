namespace CarPosAPI.Services.Trips;

/// <summary>
/// Cuts a device's fixes into trips and returns <b>only their start and end times</b> —
/// the things the database keeps. Statistics are rebuilt on read by
/// <see cref="TripSegmentAnalyzer"/> from the starts with the same
/// <see cref="StopFinder"/> walk, so the two always agree (on the end too). The end is
/// stored only so location rules can test the trip's last fix in SQL.
///
/// <para>
/// <b>The scan.</b> At each fix: if it anchors a stop, skip the whole stop; otherwise a
/// trip starts here, and <see cref="StopFinder.FindEnd"/> walks to its end. Trips shorter
/// than the minimum distance (or of a single fix) are dropped — moving the car inside a
/// car park, or GNSS drift. Then the scan continues at the stop that ended the trip, or
/// just after the hard gap that did.
/// </para>
///
/// <para>
/// <b>Restart points.</b> The scan is memoryless, so running it again from the first fix
/// of a confirmed stop, or from the first fix after a hard gap (where no lead-in leg
/// exists either way), yields exactly the starts the full run found after that point.
/// The worker uses this to recompute only the window late data touched, and to resume a
/// window it had to cut short.
/// </para>
///
/// <para>
/// No database, no clock, no logging: deliberately pure, and tested with plain tracks.
/// </para>
/// </summary>
internal static class TripDetector
{
    /// <summary>Detects trips in a window of fixes.</summary>
    /// <param name="points">
    /// Fixes in ascending time order. To recompute part of a history, the first fix must be
    /// a restart point (or the very first fix of the device).
    /// </param>
    /// <param name="settings">Thresholds.</param>
    /// <param name="moreDataAvailable">
    /// True when the window was cut short and newer fixes exist beyond it; the scan then
    /// stops at the first question those fixes could change the answer to.
    /// </param>
    /// <returns>The trips decided, and where to resume when the window was cut short.</returns>
    public static TripDetectionResult Detect(
        IReadOnlyList<TripPoint> points,
        TripDetectionSettings settings,
        bool moreDataAvailable)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(settings);

        List<DetectedTrip> trips = new List<DetectedTrip>();
        DateTime? resumeFrom = null;
        int index = 0;

        while (index < points.Count)
        {
            StopProbe probe = StopFinder.ProbeStop(points, index, settings, nowUtc: null, moreDataAvailable, needExtent: true);
            if (probe.IsStop is null)
            {
                return new TripDetectionResult(trips, resumeFrom, IsComplete: false);
            }

            if (probe.IsStop.Value)
            {
                resumeFrom = points[index].FixTime;

                // A confirmed stop that runs off the end of a cut-short window: where it
                // ends (and so where the next trip starts) is beyond what we can see.
                if (probe.ReachedEnd && moreDataAvailable)
                {
                    return new TripDetectionResult(trips, resumeFrom, IsComplete: false);
                }

                index = probe.LastIndex + 1;
                continue;
            }

            TripSegmentEnd? end = StopFinder.FindEnd(points, index, settings, nowUtc: null, moreDataAvailable);
            if (end is null)
            {
                return new TripDetectionResult(trips, resumeFrom, IsComplete: false);
            }

            TripPoint? leadIn = index > 0 ? StopFinder.LeadIn(points[index - 1], points[index], settings) : null;
            TripSegmentStats stats = TripSegmentAnalyzer.Measure(leadIn, points, index, end.EndIndex, end.Kind, settings, nowUtc: null);
            if (stats.PointCount >= 2 && stats.DistanceMeters >= settings.MinTripDistanceMeters)
            {
                // Without a clock an open end stays open: the reader may later call a
                // still spell at the very end a stop, but only a stored fix can confirm it.
                DateTime? endTime = end.Kind == TripEndKind.Open ? null : points[end.EndIndex].FixTime;
                trips.Add(new DetectedTrip(points[index].FixTime, endTime));
            }

            switch (end.Kind)
            {
                case TripEndKind.Stay:
                    // Resume at the arrival: the next probe confirms the stop and skips it.
                    index = end.EndIndex;
                    break;
                case TripEndKind.Gap:
                    // The fix after a hard gap is a restart point in its own right.
                    index = end.EndIndex + 1;
                    resumeFrom = points[index].FixTime;
                    break;
                default:
                    // Open: the scan has consumed every fix.
                    index = points.Count;
                    break;
            }
        }

        return new TripDetectionResult(trips, resumeFrom, IsComplete: true);
    }
}
