namespace CarPosAPI.Dtos;

/// <summary>
/// What a date range holds, for the "journey from dates" dialog's preview
/// (<c>GET /api/devices/{deviceId}/trips/summary</c>).
/// </summary>
/// <param name="TripCount">Trips starting in the range.</param>
/// <param name="PinnedElsewhereCount">
/// How many of them the user placed by hand somewhere other than the journey being edited
/// (including "no journey"); the dialog offers to move them too.
/// </param>
/// <param name="DistanceMeters">Their total distance, or null when <paramref name="StatsPending"/>.</param>
/// <param name="DurationSeconds">Their total duration, or null when pending.</param>
/// <param name="StatsPending">True when the totals could not be computed within the request's budget.</param>
public sealed record TripRangeSummaryDto(
    int TripCount,
    int PinnedElsewhereCount,
    double? DistanceMeters,
    double? DurationSeconds,
    bool StatsPending);
