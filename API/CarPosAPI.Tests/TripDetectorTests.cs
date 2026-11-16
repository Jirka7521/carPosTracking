using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="TripDetector"/> and the <see cref="StopFinder"/> rules it applies —
/// where one trip ends and the next begins. Each scenario is a track built the way the
/// firmware really reports: a fix every 30 s while awake, then (when parked) 15 min awake
/// followed by a timer fix every 20 min.
///
/// <para>
/// The two properties the rest of the feature depends on are tested explicitly: the
/// <b>restart property</b> (re-running from any stop reproduces the same starts, which is
/// what lets the worker recompute only a window) and <b>truncation</b> (a window cut short
/// never decides anything the missing fixes could change).
/// </para>
/// </summary>
public sealed class TripDetectorTests
{
    private static readonly DateTime Morning = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly TripDetectionSettings Defaults = TripDetectionSettings.Default;

    [Fact]
    public void TwoDrivesBetweenLongStops_AreTwoTrips_StartingAtTheFirstMovingFix()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int firstDrive = track.Points.Count;
        track.Drive(10_000, 60).Park(TimeSpan.FromMinutes(30));
        int secondDrive = track.Points.Count;
        track.Drive(5_000, 50).Park(TimeSpan.FromMinutes(30));

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.True(result.IsComplete);
        Assert.Equal(
            new[] { track.Points[firstDrive].FixTime, track.Points[secondDrive].FixTime },
            result.Starts);
    }

    [Fact]
    public void StopShorterThanTheMinimum_DoesNotSplitTheTrip()
    {
        // A 10-minute shop stop with the default 20-minute minimum.
        TripTrack track = new TripTrack(Morning)
            .Park(TimeSpan.FromMinutes(30))
            .Drive(5_000, 50)
            .Park(TimeSpan.FromMinutes(10))
            .Drive(5_000, 50)
            .Park(TimeSpan.FromMinutes(30));

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.Single(result.Starts);
    }

    [Fact]
    public void ParkingWithTheDeviceAsleep_EndsTheTrip()
    {
        // Real parking: awake 15 min, then a timer fix every 20 min from the same spot.
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        track.Drive(10_000, 60);
        ParkAndSleep(track, timerFixes: 2);
        track.Silence(TimeSpan.FromMinutes(7));
        int secondDrive = track.Points.Count;
        track.Drive(5_000, 50);
        ParkAndSleep(track, timerFixes: 2);

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.Equal(2, result.Starts.Count);
        Assert.Equal(track.Points[secondDrive].FixTime, result.Starts[1]);
    }

    [Fact]
    public void SleepGapRule_TwentyFiveMinuteStopEnds_EvenWhenTheWakeFixIsAlreadyAway()
    {
        // Stood 15 min awake, slept, and the first fix after the car left is 600 m away:
        // only 15 min of fixes at the spot, but the 10 min of sleep was parked too.
        TripTrack track = new TripTrack(Morning)
            .Park(TimeSpan.FromMinutes(30))
            .Drive(10_000, 60)
            .Park(TimeSpan.FromMinutes(15))
            .Silence(TimeSpan.FromMinutes(10))
            .MoveUnseen(600)
            .Drive(5_000, 50)
            .Park(TimeSpan.FromMinutes(30));

        Assert.Equal(2, TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false).Starts.Count);

        // With a 30-minute minimum the same 25-minute stop no longer splits.
        TripDetectionSettings longStops = Defaults with { MinStopSeconds = 30 * 60 };
        Assert.Single(TripDetector.Detect(track.Points, longStops, moreDataAvailable: false).Starts);
    }

    [Fact]
    public void ShortSilenceWhileDriving_IsBridged()
    {
        // A tunnel: six minutes with no fix while the car covers 5 km.
        TripTrack track = new TripTrack(Morning)
            .Park(TimeSpan.FromMinutes(30))
            .Drive(5_000, 60)
            .Silence(TimeSpan.FromMinutes(6))
            .MoveUnseen(5_000)
            .Drive(5_000, 60)
            .Park(TimeSpan.FromMinutes(30));

        Assert.Single(TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false).Starts);
    }

    [Fact]
    public void LongSilenceAcrossWhichTheCarMoved_SplitsTheTrip()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int firstDrive = track.Points.Count;
        track.Drive(5_000, 60).Silence(TimeSpan.FromHours(3)).MoveUnseen(50_000);
        int afterGap = track.Points.Count;
        track.Drive(5_000, 60).Park(TimeSpan.FromMinutes(30));

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.Equal(
            new[] { track.Points[firstDrive].FixTime, track.Points[afterGap].FixTime },
            result.Starts);
    }

    [Fact]
    public void ShortMovement_BelowTheMinimumDistance_IsNotATrip()
    {
        // Re-parking 400 m away inside a car park, with GNSS jitter while parked.
        TripTrack track = new TripTrack(Morning)
            .Park(TimeSpan.FromMinutes(30), jitterMeters: 20)
            .Drive(400, 20)
            .Park(TimeSpan.FromMinutes(30), jitterMeters: 20);

        Assert.Empty(TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false).Starts);

        TripDetectionSettings keepEverything = Defaults with { MinTripDistanceMeters = 0 };
        Assert.Single(TripDetector.Detect(track.Points, keepEverything, moreDataAvailable: false).Starts);
    }

    [Fact]
    public void ParkedJitter_NeverBecomesATrip()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromHours(6), jitterMeters: 40);

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.Empty(result.Starts);
        Assert.Equal(track.Points[0].FixTime, result.ResumeFrom);
    }

    [Fact]
    public void DriveStillGoing_IsAnOpenTripWhoseStartIsRecorded()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int firstDrive = track.Points.Count;
        track.Drive(3_000, 40);

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.True(result.IsComplete);
        Assert.Equal(new[] { track.Points[firstDrive].FixTime }, result.Starts);
    }

    [Fact]
    public void DriveStillGoing_HasNoStoredEndYet()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30)).Drive(3_000, 40);

        DetectedTrip trip = Assert.Single(TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false).Trips);

        Assert.Null(trip.End);
    }

    [Fact]
    public void LongSilenceAcrossWhichTheCarMoved_EndsTheFirstTripAtTheLastFixBeforeIt()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        track.Drive(5_000, 60).Silence(TimeSpan.FromHours(3)).MoveUnseen(50_000);
        int afterGap = track.Points.Count;
        track.Drive(5_000, 60).Park(TimeSpan.FromMinutes(30));

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.Equal(track.Points[afterGap - 1].FixTime, result.Trips[0].End);
    }

    [Fact]
    public void StoredEnd_IsTheEndTheReadPathFinds_ForEveryDecidedTrip()
    {
        // The end is stored so location rules can test it in SQL; it must be the very fix
        // the dashboard shows as the trip's end, or a rule would judge a different place.
        IReadOnlyList<TripPoint> points = BusyDay().Points;
        IReadOnlyList<DetectedTrip> trips = TripDetector.Detect(points, Defaults, moreDataAvailable: false).Trips;
        IReadOnlyList<DateTime> starts = trips.Select(trip => trip.Start).ToList();

        Assert.Contains(trips, trip => trip.End is null);
        for (int trip = 0; trip < trips.Count; trip++)
        {
            if (trips[trip].End is DateTime storedEnd)
            {
                TripSegmentStats stats = AnalyzeTrip(points, starts, trip);
                Assert.NotEqual(TripEndKind.Open, stats.EndKind);
                Assert.Equal(stats.End, storedEnd);
                Assert.True(storedEnd > trips[trip].Start);
            }
            else
            {
                // Only the newest trip can still be open.
                Assert.Equal(trips.Count - 1, trip);
            }
        }
    }

    [Fact]
    public void HistoryStartingMidDrive_StartsATripAtTheFirstFix()
    {
        TripTrack track = new TripTrack(Morning).Drive(5_000, 50).Park(TimeSpan.FromMinutes(30));

        TripDetectionResult result = TripDetector.Detect(track.Points, Defaults, moreDataAvailable: false);

        Assert.Equal(new[] { track.Points[0].FixTime }, result.Starts);
    }

    [Fact]
    public void RestartingFromAnyTripsPrecedingStop_ReproducesTheSameStarts()
    {
        IReadOnlyList<TripPoint> points = BusyDay().Points;
        IReadOnlyList<DetectedTrip> fullTrips = TripDetector.Detect(points, Defaults, moreDataAvailable: false).Trips;
        IReadOnlyList<DateTime> full = fullTrips.Select(trip => trip.Start).ToList();
        Assert.True(full.Count >= 5, "the scenario should produce several trips");

        // For each trip after the first, restart where the previous trip ended: at the
        // arrival (the first fix of the stop) after a stop, or at the first fix after the
        // silence after a hard gap. Re-running from there must find exactly the trips —
        // starts and stored ends — from this trip onward.
        Assert.Contains(
            Enumerable.Range(0, full.Count).Select(trip => AnalyzeTrip(points, full, trip).EndKind),
            kind => kind == TripEndKind.Gap);
        for (int trip = 1; trip < full.Count; trip++)
        {
            TripSegmentStats previous = AnalyzeTrip(points, full, trip - 1);
            Assert.NotEqual(TripEndKind.Open, previous.EndKind);

            int restartIndex = IndexOf(points, previous.End) + (previous.EndKind == TripEndKind.Gap ? 1 : 0);
            List<TripPoint> window = points.Skip(restartIndex).ToList();
            IReadOnlyList<DetectedTrip> restarted = TripDetector.Detect(window, Defaults, moreDataAvailable: false).Trips;

            Assert.Equal(fullTrips.Skip(trip), restarted);
        }
    }

    [Fact]
    public void WindowCutShort_DecidesOnlyWhatTheMissingFixesCannotChange_AndResumesExactly()
    {
        IReadOnlyList<TripPoint> points = BusyDay().Points;
        IReadOnlyList<DetectedTrip> full = TripDetector.Detect(points, Defaults, moreDataAvailable: false).Trips;

        for (int cut = 1; cut < points.Count; cut += 7)
        {
            List<TripPoint> prefix = points.Take(cut).ToList();
            TripDetectionResult partial = TripDetector.Detect(prefix, Defaults, moreDataAvailable: true);

            // Starts and ends alike: a cut-short window never stores an end the missing
            // fixes could still move.
            List<DetectedTrip> combined = new List<DetectedTrip>(partial.Trips);
            if (!partial.IsComplete)
            {
                if (partial.ResumeFrom is not DateTime resumeAt)
                {
                    // Nothing confirmed yet: nothing may have been decided either.
                    Assert.Empty(partial.Trips);
                    continue;
                }

                Assert.All(partial.Starts, start => Assert.True(start < resumeAt));
                List<TripPoint> rest = points.Skip(IndexOf(points, resumeAt)).ToList();
                combined.AddRange(TripDetector.Detect(rest, Defaults, moreDataAvailable: false).Trips);
            }

            Assert.Equal(full, combined);
        }
    }

    [Fact]
    public void Distance_IsTheGreatCircle()
    {
        // One degree of latitude on the IUGG mean sphere.
        Assert.Equal(111_195.08, GeoDistance.Meters(0, 0, 1, 0), 1);
    }

    /// <summary>
    /// A day with everything in it: parking with sleep, a short stop, a tunnel, a car-park
    /// shuffle, a long silence across which the car moved, and an open trip at the end.
    /// </summary>
    internal static TripTrack BusyDay()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(40));
        track.Drive(12_000, 70);
        ParkAndSleep(track, timerFixes: 3);
        track.Silence(TimeSpan.FromMinutes(4)).Drive(3_000, 30).Park(TimeSpan.FromMinutes(8)).Drive(4_000, 45);
        ParkAndSleep(track, timerFixes: 1);
        track.Silence(TimeSpan.FromMinutes(9)).Drive(20_000, 90).Silence(TimeSpan.FromMinutes(6)).MoveUnseen(6_000).Drive(8_000, 90);
        track.Park(TimeSpan.FromMinutes(15)).Silence(TimeSpan.FromMinutes(12)).MoveUnseen(800).Drive(2_000, 40);
        ParkAndSleep(track, timerFixes: 2);
        track.Drive(300, 15);
        ParkAndSleep(track, timerFixes: 2);
        track.Drive(6_000, 50).Silence(TimeSpan.FromHours(3)).MoveUnseen(40_000).Drive(7_000, 60);
        ParkAndSleep(track, timerFixes: 1);
        track.Silence(TimeSpan.FromMinutes(5)).Drive(9_000, 55);
        return track;
    }

    /// <summary>Firmware parking: awake for 15 min, then a timer fix every 20 min.</summary>
    internal static void ParkAndSleep(TripTrack track, int timerFixes)
    {
        track.Park(TimeSpan.FromMinutes(15));
        for (int fix = 0; fix < timerFixes; fix++)
        {
            track.Silence(TimeSpan.FromMinutes(20)).Fix();
        }
    }

    /// <summary>Runs the analyzer the way the read path will for trip <paramref name="trip"/>.</summary>
    internal static TripSegmentStats AnalyzeTrip(IReadOnlyList<TripPoint> points, IReadOnlyList<DateTime> starts, int trip)
    {
        int startIndex = IndexOf(points, starts[trip]);
        bool hasNext = trip + 1 < starts.Count;
        int lastIndex = hasNext ? IndexOf(points, starts[trip + 1]) : points.Count - 1;
        TripPoint? previous = startIndex > 0 ? points[startIndex - 1] : null;
        List<TripPoint> slice = points.Skip(startIndex).Take(lastIndex - startIndex + 1).ToList();

        TripSegmentStats? stats = TripSegmentAnalyzer.Analyze(
            previous,
            slice,
            Defaults,
            nowUtc: hasNext ? null : slice[^1].FixTime,
            moreDataAvailable: false,
            lastPointStartsNextTrip: hasNext);

        Assert.NotNull(stats);
        return stats;
    }

    internal static int IndexOf(IReadOnlyList<TripPoint> points, DateTime fixTime)
    {
        for (int index = 0; index < points.Count; index++)
        {
            if (points[index].FixTime == fixTime)
            {
                return index;
            }
        }

        throw new InvalidOperationException("No fix at that time.");
    }
}
