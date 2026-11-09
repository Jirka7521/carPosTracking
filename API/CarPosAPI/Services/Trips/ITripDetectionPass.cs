namespace CarPosAPI.Services.Trips;

/// <summary>
/// One unit of the trip worker's work: fold newly saved positions into each device's
/// dirty range, then recompute the trip starts of dirty devices within a time budget.
/// Scoped — it owns a <see cref="Data.CarPosDbContext"/> for the pass.
/// </summary>
internal interface ITripDetectionPass
{
    /// <summary>Runs one pass.</summary>
    /// <param name="utcNow">The pass instant, stamped on what it writes.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What the pass did, and whether work is left.</returns>
    Task<TripPassOutcome> RunAsync(DateTime utcNow, CancellationToken cancellationToken);
}
