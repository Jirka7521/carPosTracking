namespace CarPosAPI.Services.Trips;

/// <summary>
/// Rebuilds many stored trips at once — a list page, a journey's totals — reading the
/// device's fixes in as few bounded queries as it can. Scoped.
/// </summary>
internal interface ITripStatsCalculator
{
    /// <summary>Computes each trip's end and statistics.</summary>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="spans">The trips, any order.</param>
    /// <param name="settings">The device's thresholds.</param>
    /// <param name="nowUtc">The current time (for the newest trip).</param>
    /// <param name="maxFixes">Most fixes to read for the whole call.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// Statistics per trip id; null for a trip that could not be rebuilt within the budget
    /// (or whose start fix has just been erased).
    /// </returns>
    Task<Dictionary<long, TripSegmentStats?>> AnalyzeAsync(
        Guid deviceRowId,
        IReadOnlyList<TripSpan> spans,
        TripDetectionSettings settings,
        DateTime nowUtc,
        int maxFixes,
        CancellationToken cancellationToken);
}
