using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="TripSegmentAnalyzer"/> — the read path that rebuilds a trip's end
/// and statistics from its stored start. The central guarantee: for every start the
/// detector stores, the analyzer finds a real end before the next trip begins, so a
/// database holding nothing but starts loses no information.
/// </summary>
public sealed class TripSegmentAnalyzerTests
{
    private static readonly DateTime Morning = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly TripDetectionSettings Defaults = TripDetectionSettings.Default;

    [Fact]
    public void EveryDetectedTrip_EndsBeforeTheNextOneStarts_AndMeetsTheMinimumDistance()
    {
        IReadOnlyList<TripPoint> points = TripDetectorTests.BusyDay().Points;
        IReadOnlyList<DateTime> starts = TripDetector.Detect(points, Defaults, moreDataAvailable: false).Starts;

        for (int trip = 0; trip < starts.Count; trip++)
        {
            TripSegmentStats stats = TripDetectorTests.AnalyzeTrip(points, starts, trip);

            Assert.True(stats.DistanceMeters >= Defaults.MinTripDistanceMeters);
            Assert.True(stats.End >= starts[trip]);
            if (trip + 1 < starts.Count)
            {
                Assert.NotEqual(TripEndKind.Open, stats.EndKind);
                Assert.True(stats.End < starts[trip + 1]);
            }
        }
    }

    [Fact]
    public void ConstantSpeedDrive_MeasuresItsDistanceAndSpeeds()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int start = track.Points.Count;
        track.Drive(10_000, 60).Park(TimeSpan.FromMinutes(30));

        TripSegmentStats stats = Analyze(track, start);

        Assert.Equal(TripEndKind.Stay, stats.EndKind);
        Assert.False(stats.InProgress);
        Assert.False(stats.HasGap);
        Assert.InRange(stats.DistanceMeters, 9_950, 10_050);
        Assert.Equal(60, stats.MaxSpeedKmph);
        Assert.NotNull(stats.AverageSpeedKmph);
        Assert.InRange(stats.AverageSpeedKmph.Value, 54, 61);
        Assert.Equal(stats.DurationSeconds, stats.MovingSeconds, 3);
    }

    [Fact]
    public void LeavingWhileAwake_DepartsAtTheLastParkedFix()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int start = track.Points.Count;
        track.Drive(5_000, 50).Park(TimeSpan.FromMinutes(30));

        TripSegmentStats stats = Analyze(track, start);

        Assert.Equal(track.Points[start - 1].FixTime, stats.Departure);
    }

    [Fact]
    public void LeavingAfterASleep_DepartsAtTheFirstMovingFix_ButCountsTheWayOut()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30)).Silence(TimeSpan.FromMinutes(15)).MoveUnseen(1_000);
        int start = track.Points.Count;
        track.Drive(5_000, 50).Park(TimeSpan.FromMinutes(30));

        TripSegmentStats stats = Analyze(track, start);

        Assert.Equal(track.Points[start].FixTime, stats.Departure);
        Assert.InRange(stats.DistanceMeters, 5_950, 6_050);
    }

    [Fact]
    public void TunnelInsideTheTrip_IsFlaggedAsAGap_AndNotCountedAsMoving()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int start = track.Points.Count;
        track.Drive(5_000, 60).Silence(TimeSpan.FromMinutes(6)).MoveUnseen(5_000).Drive(5_000, 60).Park(TimeSpan.FromMinutes(30));

        TripSegmentStats stats = Analyze(track, start);

        Assert.True(stats.HasGap);
        Assert.InRange(stats.DistanceMeters, 14_900, 15_100);
        Assert.True(stats.MovingSeconds < stats.DurationSeconds - 300);
    }

    [Fact]
    public void LongSilence_EndsTheTripAsAGap()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int start = track.Points.Count;
        track.Drive(5_000, 60);
        int lastBeforeGap = track.Points.Count - 1;
        track.Silence(TimeSpan.FromHours(3)).MoveUnseen(50_000).Drive(5_000, 60).Park(TimeSpan.FromMinutes(30));

        TripSegmentStats stats = Analyze(track, start);

        Assert.Equal(TripEndKind.Gap, stats.EndKind);
        Assert.Equal(track.Points[lastBeforeGap].FixTime, stats.End);
    }

    [Fact]
    public void NewestTrip_IsInProgressUntilTheMinimumStopHasPassed()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int start = track.Points.Count;
        track.Drive(3_000, 40);
        DateTime lastFix = track.Points[^1].FixTime;

        TripSegmentStats driving = Analyze(track, start, now: lastFix.AddMinutes(1));
        Assert.Equal(TripEndKind.Open, driving.EndKind);
        Assert.True(driving.InProgress);

        TripSegmentStats silent = Analyze(track, start, now: lastFix.AddMinutes(25));
        Assert.Equal(TripEndKind.Open, silent.EndKind);
        Assert.False(silent.InProgress);
    }

    [Fact]
    public void NewestTrip_EndsOnceTheParkedDeviceHasSleptLongEnough()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int start = track.Points.Count;
        track.Drive(10_000, 60);
        DateTime arrival = track.Points[^1].FixTime;
        track.Park(TimeSpan.FromMinutes(10));

        // Twelve minutes after arriving: two minutes of silence is not yet sleep.
        TripSegmentStats early = Analyze(track, start, now: arrival.AddMinutes(12));
        Assert.Equal(TripEndKind.Open, early.EndKind);
        Assert.True(early.InProgress);

        // Twenty-five minutes after arriving: ten minutes still, fifteen asleep — a stop.
        TripSegmentStats later = Analyze(track, start, now: arrival.AddMinutes(25));
        Assert.Equal(TripEndKind.Stay, later.EndKind);
        Assert.Equal(arrival, later.End);
        Assert.False(later.InProgress);
    }

    [Fact]
    public void TruncatedInput_AsksForMoreInsteadOfGuessing()
    {
        TripTrack track = new TripTrack(Morning).Park(TimeSpan.FromMinutes(30));
        int start = track.Points.Count;
        track.Drive(10_000, 60).Park(TimeSpan.FromMinutes(30));
        List<TripPoint> firstFew = track.Points.Skip(start).Take(5).ToList();

        TripSegmentStats? stats = TripSegmentAnalyzer.Analyze(
            track.Points[start - 1],
            firstFew,
            Defaults,
            nowUtc: null,
            moreDataAvailable: true,
            lastPointStartsNextTrip: false);

        Assert.Null(stats);
    }

    private static TripSegmentStats Analyze(TripTrack track, int startIndex, DateTime? now = null)
    {
        List<TripPoint> slice = track.Points.Skip(startIndex).ToList();
        TripSegmentStats? stats = TripSegmentAnalyzer.Analyze(
            track.Points[startIndex - 1],
            slice,
            Defaults,
            now ?? slice[^1].FixTime,
            moreDataAvailable: false,
            lastPointStartsNextTrip: false);

        Assert.NotNull(stats);
        return stats;
    }
}
