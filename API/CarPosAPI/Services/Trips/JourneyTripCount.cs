namespace CarPosAPI.Services.Trips;

/// <summary>How many of a device's trips are in one journey right now — one row of a grouped count.</summary>
/// <param name="JourneyId">The effective journey, or null for "in no journey".</param>
/// <param name="TripCount">Trips directly in it.</param>
internal sealed record JourneyTripCount(Guid? JourneyId, int TripCount);
