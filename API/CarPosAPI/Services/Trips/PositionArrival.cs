namespace CarPosAPI.Services.Trips;

/// <summary>
/// The positions one device gained since the trip worker's cursor, summarised for the
/// dirty marker: the earliest fix among them (where recomputation must reach back to)
/// and the highest id (where the cursor may advance to).
/// </summary>
/// <param name="DeviceId">The device's internal row id.</param>
/// <param name="EarliestFixTime">Earliest fix time among the new rows (UTC).</param>
/// <param name="LastPositionId">Highest position id among them.</param>
/// <param name="Count">How many new rows.</param>
internal sealed record PositionArrival(Guid DeviceId, DateTime EarliestFixTime, long LastPositionId, int Count);
