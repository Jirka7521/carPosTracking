namespace CarPosAPI.Dtos;

/// <summary>
/// A journey's totals, including every journey inside it
/// (<c>GET /api/devices/{deviceId}/journeys/{journeyId}/summary</c>). Computed from the
/// positions on request; nothing here is stored.
/// </summary>
/// <param name="TripCount">Trips in the journey and its sub-journeys.</param>
/// <param name="DistanceMeters">Total distance, or null when <paramref name="StatsPending"/>.</param>
/// <param name="DurationSeconds">Total duration, or null when pending.</param>
/// <param name="MovingSeconds">Total moving time, or null when pending.</param>
/// <param name="StatsPending">True when the totals could not be computed within the request's budget.</param>
public sealed record JourneySummaryDto(
    int TripCount,
    double? DistanceMeters,
    double? DurationSeconds,
    double? MovingSeconds,
    bool StatsPending);
