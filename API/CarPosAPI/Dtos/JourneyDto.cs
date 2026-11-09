namespace CarPosAPI.Dtos;

/// <summary>
/// One journey in the flat list the dashboard builds its tree from. Counts only — the
/// distance and time totals come from the journey's summary endpoint, on demand.
/// </summary>
/// <param name="Id">Journey id.</param>
/// <param name="ParentId">The enclosing journey, or null at the top level.</param>
/// <param name="Name">Display name.</param>
/// <param name="RangeFrom">Inclusive start of the auto-include range (UTC), or null.</param>
/// <param name="RangeTo">Exclusive end of the auto-include range (UTC), or null.</param>
/// <param name="TripCount">Trips directly in this journey.</param>
/// <param name="TotalTripCount">Trips in this journey and all journeys inside it.</param>
public sealed record JourneyDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    DateTime? RangeFrom,
    DateTime? RangeTo,
    int TripCount,
    int TotalTripCount);
