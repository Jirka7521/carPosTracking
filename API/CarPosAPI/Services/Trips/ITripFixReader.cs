namespace CarPosAPI.Services.Trips;

/// <summary>
/// Reads a device's fixes for the trip logic, always bounded by device, time and row
/// count — <c>positions</c> grows without bound, so no read here may be open-ended.
/// Shared by the detection worker and the read path so both see fixes the same way.
/// Scoped — it owns the scope's <see cref="Data.CarPosDbContext"/>.
/// </summary>
internal interface ITripFixReader
{
    /// <summary>Fixes from <paramref name="fromInclusive"/> onward, oldest first.</summary>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="fromInclusive">First fix time to include, or null for the device's very first fix.</param>
    /// <param name="limit">Most rows to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Up to <paramref name="limit"/> fixes.</returns>
    Task<List<TripPoint>> ReadFromAsync(Guid deviceRowId, DateTime? fromInclusive, int limit, CancellationToken cancellationToken);

    /// <summary>The last fix strictly before <paramref name="before"/>, or null.</summary>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="before">The exclusive upper bound.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>That fix, or null when there is none.</returns>
    Task<TripPoint?> ReadBeforeAsync(Guid deviceRowId, DateTime before, CancellationToken cancellationToken);

    /// <summary>The first fix strictly after <paramref name="after"/>, or null.</summary>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="after">The exclusive lower bound.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>That fix's time, or null when there is none.</returns>
    Task<DateTime?> FirstFixAfterAsync(Guid deviceRowId, DateTime after, CancellationToken cancellationToken);

    /// <summary>
    /// Rebuilds one stored trip: loads its fixes in chunks — from its start up to and
    /// including the next trip's start — until <see cref="TripSegmentAnalyzer"/> can
    /// decide where it ended, and measures it.
    /// </summary>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="start">The trip's stored start.</param>
    /// <param name="nextStart">The next trip's stored start, or null for the newest trip.</param>
    /// <param name="settings">The device's thresholds.</param>
    /// <param name="nowUtc">The current time for the newest trip; ignored when <paramref name="nextStart"/> is set.</param>
    /// <param name="maxFixes">Most fixes to load for this one trip.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The trip's end and statistics, or null when its start fix no longer exists or the cap was reached.</returns>
    Task<TripSegmentStats?> AnalyzeAsync(
        Guid deviceRowId,
        DateTime start,
        DateTime? nextStart,
        TripDetectionSettings settings,
        DateTime? nowUtc,
        int maxFixes,
        CancellationToken cancellationToken);
}
