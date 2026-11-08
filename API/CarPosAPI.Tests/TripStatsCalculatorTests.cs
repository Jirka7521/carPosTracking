using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="TripStatsCalculator"/> — the windowed bulk rebuild behind every list
/// page and journey total. The guarantee tested: however the fixes are cut into windows,
/// every trip comes out exactly as if it had been rebuilt on its own, and a budget that
/// runs out marks the rest pending instead of reading past it.
/// </summary>
public sealed class TripStatsCalculatorTests
{
    private static readonly Guid Device = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData(25_000)]
    [InlineData(200)]
    [InlineData(37)]
    [InlineData(5)]
    public async Task AnyWindowSize_RebuildsEveryTripExactlyAsOnItsOwn(int windowFixes)
    {
        IReadOnlyList<TripPoint> points = TripDetectorTests.BusyDay().Points;
        IReadOnlyList<DateTime> starts = TripDetector.Detect(points, TripDetectionSettings.Default, moreDataAvailable: false).Starts;
        List<TripSpan> spans = Spans(starts);
        DateTime now = points[^1].FixTime;

        TripStatsCalculator calculator = new TripStatsCalculator(new InMemoryTripFixReader(points), windowFixes);
        Dictionary<long, TripSegmentStats?> results = await calculator.AnalyzeAsync(
            Device,
            spans,
            TripDetectionSettings.Default,
            now,
            maxFixes: 1_000_000,
            CancellationToken.None);

        for (int trip = 0; trip < starts.Count; trip++)
        {
            TripSegmentStats alone = TripDetectorTests.AnalyzeTrip(points, starts, trip);
            Assert.Equal(alone, results[trip]);
        }
    }

    [Fact]
    public async Task ASubsetOfTrips_IsRebuiltCorrectly_SkippingTheOthers()
    {
        IReadOnlyList<TripPoint> points = TripDetectorTests.BusyDay().Points;
        IReadOnlyList<DateTime> starts = TripDetector.Detect(points, TripDetectionSettings.Default, moreDataAvailable: false).Starts;
        List<TripSpan> everyOther = Spans(starts).Where(span => span.Id % 2 == 0).ToList();

        TripStatsCalculator calculator = new TripStatsCalculator(new InMemoryTripFixReader(points), windowFixes: 50);
        Dictionary<long, TripSegmentStats?> results = await calculator.AnalyzeAsync(
            Device,
            everyOther,
            TripDetectionSettings.Default,
            points[^1].FixTime,
            maxFixes: 1_000_000,
            CancellationToken.None);

        Assert.Equal(everyOther.Count, results.Count);
        foreach (TripSpan span in everyOther)
        {
            Assert.Equal(TripDetectorTests.AnalyzeTrip(points, starts, (int)span.Id), results[span.Id]);
        }
    }

    [Fact]
    public async Task ABudgetThatRunsOut_MarksTheRestPending_AndIsRespected()
    {
        IReadOnlyList<TripPoint> points = TripDetectorTests.BusyDay().Points;
        IReadOnlyList<DateTime> starts = TripDetector.Detect(points, TripDetectionSettings.Default, moreDataAvailable: false).Starts;
        InMemoryTripFixReader reader = new InMemoryTripFixReader(points);
        const int Budget = 120;

        TripStatsCalculator calculator = new TripStatsCalculator(reader, windowFixes: 40);
        Dictionary<long, TripSegmentStats?> results = await calculator.AnalyzeAsync(
            Device,
            Spans(starts),
            TripDetectionSettings.Default,
            points[^1].FixTime,
            maxFixes: Budget,
            CancellationToken.None);

        Assert.Equal(starts.Count, results.Count);
        Assert.Contains(results.Values, stats => stats is null);
        Assert.Contains(results.Values, stats => stats is not null);
        Assert.True(reader.FixesRead <= Budget + 40, $"read {reader.FixesRead} fixes on a budget of {Budget}");
    }

    private static List<TripSpan> Spans(IReadOnlyList<DateTime> starts) =>
        starts
            .Select((start, index) => new TripSpan(index, start, index + 1 < starts.Count ? starts[index + 1] : null))
            .ToList();
}
