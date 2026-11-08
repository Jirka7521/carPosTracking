namespace CarPosAPI.Services.Trips;

/// <summary>A stored trip's stretch of history: from its start to the next trip's start.</summary>
/// <param name="Id">Trip id.</param>
/// <param name="Start">The stored start.</param>
/// <param name="NextStart">The next trip's start, or null for the newest trip.</param>
internal sealed record TripSpan(long Id, DateTime Start, DateTime? NextStart);
