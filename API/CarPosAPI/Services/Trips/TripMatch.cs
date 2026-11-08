namespace CarPosAPI.Services.Trips;

/// <summary>
/// What to write for one newly detected trip start.
/// </summary>
/// <param name="StartTime">The detected start.</param>
/// <param name="ExistingTripId">
/// The row to update in place (keeping its id, name and placement), or null to insert a
/// new row.
/// </param>
/// <param name="JourneyId">The journey to store — kept or inherited from the old trip.</param>
/// <param name="JourneyPinned">Whether the placement is the user's (kept or inherited).</param>
/// <param name="Name">The name to keep, or null.</param>
internal sealed record TripMatch(DateTime StartTime, long? ExistingTripId, Guid? JourneyId, bool JourneyPinned, string? Name);
