using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// An <see cref="ITripFixReader"/> over a list of fixes, so the code that reads fixes in
/// bounded windows can be tested against plain tracks with no database. Counts the fixes
/// it hands out, so tests can check a budget was respected.
/// </summary>
internal sealed class InMemoryTripFixReader : ITripFixReader
{
    private readonly IReadOnlyList<TripPoint> _points;

    /// <summary>Wraps a track, sorted by time.</summary>
    /// <param name="points">The device's fixes.</param>
    public InMemoryTripFixReader(IReadOnlyList<TripPoint> points)
    {
        _points = points.OrderBy(point => point.FixTime).ToList();
    }

    /// <summary>Total fixes returned by <see cref="ReadFromAsync"/> so far.</summary>
    public int FixesRead { get; private set; }

    /// <inheritdoc />
    public Task<List<TripPoint>> ReadFromAsync(Guid deviceRowId, DateTime? fromInclusive, int limit, CancellationToken cancellationToken)
    {
        List<TripPoint> result = _points
            .Where(point => fromInclusive is null || point.FixTime >= fromInclusive.Value)
            .Take(limit)
            .ToList();
        FixesRead += result.Count;
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<TripPoint?> ReadBeforeAsync(Guid deviceRowId, DateTime before, CancellationToken cancellationToken) =>
        Task.FromResult(_points.LastOrDefault(point => point.FixTime < before));

    /// <inheritdoc />
    public Task<DateTime?> FirstFixAfterAsync(Guid deviceRowId, DateTime after, CancellationToken cancellationToken) =>
        Task.FromResult(_points.Where(point => point.FixTime > after).Select(point => (DateTime?)point.FixTime).FirstOrDefault());

    /// <inheritdoc />
    public Task<TripSegmentStats?> AnalyzeAsync(
        Guid deviceRowId,
        DateTime start,
        DateTime? nextStart,
        TripDetectionSettings settings,
        DateTime? nowUtc,
        int maxFixes,
        CancellationToken cancellationToken)
    {
        List<TripPoint> slice = _points
            .Where(point => point.FixTime >= start && (nextStart is null || point.FixTime <= nextStart.Value))
            .ToList();
        if (slice.Count == 0 || slice[0].FixTime != start || slice.Count > maxFixes)
        {
            return Task.FromResult<TripSegmentStats?>(null);
        }

        return Task.FromResult(TripSegmentAnalyzer.Analyze(
            _points.LastOrDefault(point => point.FixTime < start),
            slice,
            settings,
            nextStart is null ? nowUtc : null,
            moreDataAvailable: false,
            lastPointStartsNextTrip: nextStart is DateTime next && slice[^1].FixTime == next));
    }
}
