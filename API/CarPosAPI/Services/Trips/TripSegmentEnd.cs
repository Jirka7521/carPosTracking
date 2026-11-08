namespace CarPosAPI.Services.Trips;

/// <summary>Where a trip's scan ended, as found by <see cref="StopFinder.FindEnd"/>.</summary>
/// <param name="EndIndex">
/// Index of the trip's last fix: for <see cref="TripEndKind.Stay"/> the first fix of the
/// stop (the arrival), for <see cref="TripEndKind.Gap"/> the last fix before the
/// silence, for <see cref="TripEndKind.Open"/> the last fix available.
/// </param>
/// <param name="Kind">How the trip ended.</param>
internal sealed record TripSegmentEnd(int EndIndex, TripEndKind Kind);
