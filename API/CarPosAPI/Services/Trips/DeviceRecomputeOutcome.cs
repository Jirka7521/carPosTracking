namespace CarPosAPI.Services.Trips;

/// <summary>What recomputing one device's trips did within a pass.</summary>
/// <param name="TripsChanged">Trip rows inserted, moved or deleted.</param>
/// <param name="Finished">False when the device is still dirty (budget ran out, or the work was invalidated).</param>
internal sealed record DeviceRecomputeOutcome(int TripsChanged, bool Finished);
