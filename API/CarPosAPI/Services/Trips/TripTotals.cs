namespace CarPosAPI.Services.Trips;

/// <summary>Summed statistics of several trips, for a range or journey summary.</summary>
/// <param name="DistanceMeters">Total distance of the trips that could be rebuilt.</param>
/// <param name="DurationSeconds">Total duration of those trips.</param>
/// <param name="MovingSeconds">Total moving time of those trips.</param>
/// <param name="Pending">True when at least one trip could not be rebuilt, so the totals are incomplete.</param>
internal sealed record TripTotals(double DistanceMeters, double DurationSeconds, double MovingSeconds, bool Pending);
