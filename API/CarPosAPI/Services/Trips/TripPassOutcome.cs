namespace CarPosAPI.Services.Trips;

/// <summary>What one pass of <see cref="ITripDetectionPass"/> did.</summary>
/// <param name="DevicesProcessed">Devices whose trips were recomputed.</param>
/// <param name="TripsChanged">Trip rows inserted, moved or deleted.</param>
/// <param name="MoreWork">
/// True when the pass stopped on a budget with work left (a long backfill, or many dirty
/// devices); the worker then runs the next pass immediately instead of waiting for a save.
/// </param>
internal sealed record TripPassOutcome(int DevicesProcessed, int TripsChanged, bool MoreWork);
