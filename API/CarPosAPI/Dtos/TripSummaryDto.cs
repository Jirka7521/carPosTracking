namespace CarPosAPI.Dtos;

/// <summary>
/// One trip as the Trips tab shows it. Only <paramref name="Id"/>,
/// <paramref name="StartTime"/> and the user's organisation are stored; everything else
/// is recomputed from the device's positions for this response.
/// </summary>
/// <param name="Id">Stable trip id — survives recomputation; the detail page's URL key.</param>
/// <param name="StartTime">The trip's first moving fix (UTC) — the stored start and the list's order.</param>
/// <param name="Departure">
/// When the car left (UTC): the last parked fix when the car left while the tracker was
/// awake, otherwise <paramref name="StartTime"/>. Null while <paramref name="StatsPending"/>.
/// </param>
/// <param name="EndTime">The arrival, or the last fix for an open trip (UTC). Null while pending.</param>
/// <param name="EndKind">See <see cref="TripEndKindNames"/>. Null while pending.</param>
/// <param name="InProgress">True while the trip may still be going.</param>
/// <param name="DistanceMeters">Driven distance. Null while pending.</param>
/// <param name="DurationSeconds">From departure to end. Null while pending.</param>
/// <param name="MovingSeconds">Time actually moving. Null while pending.</param>
/// <param name="AverageSpeedKmph">Distance over duration; null for a zero-length trip or while pending.</param>
/// <param name="MovingAverageSpeedKmph">Distance over moving time; null when never moving or while pending.</param>
/// <param name="MaxSpeedKmph">Highest receiver speed. Null while pending.</param>
/// <param name="HasGap">True when the trip bridges a data gap across which the car moved.</param>
/// <param name="PointCount">Fixes in the trip. Null while pending.</param>
/// <param name="JourneyId">
/// The journey the trip is in right now — chosen by hand, or by the innermost journey
/// whose date range holds the start — or null for none.
/// </param>
/// <param name="JourneyPinned">True when the user placed the trip by hand; ranges then never move it.</param>
/// <param name="Name">The user's name for the trip, or null.</param>
/// <param name="StatsPending">
/// True when the statistics could not be computed in this response (the per-request
/// budget ran out, or the trip is being recomputed). Fetch the trip on its own to fill in.
/// </param>
public sealed record TripSummaryDto(
    long Id,
    DateTime StartTime,
    DateTime? Departure,
    DateTime? EndTime,
    string? EndKind,
    bool InProgress,
    double? DistanceMeters,
    double? DurationSeconds,
    double? MovingSeconds,
    double? AverageSpeedKmph,
    double? MovingAverageSpeedKmph,
    double? MaxSpeedKmph,
    bool HasGap,
    int? PointCount,
    Guid? JourneyId,
    bool JourneyPinned,
    string? Name,
    bool StatsPending);
