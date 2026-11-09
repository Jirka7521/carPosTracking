namespace CarPosAPI.Services.Trips;

/// <summary>
/// An existing trip row as <see cref="TripMetadataMatcher"/> needs it: identity, start,
/// and the user's data that must survive a recompute.
/// </summary>
/// <param name="Id">The row id (kept when the trip is matched).</param>
/// <param name="StartTime">The stored start.</param>
/// <param name="JourneyId">The journey the user pinned it to, or null.</param>
/// <param name="JourneyPinned">Whether the user placed it by hand.</param>
/// <param name="Name">The user's name for it, or null.</param>
internal sealed record StoredTripSnapshot(long Id, DateTime StartTime, Guid? JourneyId, bool JourneyPinned, string? Name)
{
    /// <summary>True when the user has touched this trip — such rows win a merge.</summary>
    public bool HasUserData => JourneyPinned || Name is not null;
}
