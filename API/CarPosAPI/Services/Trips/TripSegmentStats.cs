namespace CarPosAPI.Services.Trips;

/// <summary>
/// One trip's end and statistics, derived from its fixes by
/// <see cref="TripSegmentAnalyzer"/>. Nothing here is stored; it is recomputed on read.
/// </summary>
/// <param name="Departure">
/// When the trip began: the last fix of the preceding stop if the car left within a
/// data gap of it, otherwise the first moving fix.
/// </param>
/// <param name="End">Time of the trip's last fix (the arrival, for a trip ended by a stop).</param>
/// <param name="EndKind">How the trip ended.</param>
/// <param name="InProgress">True for an open trip whose last fix is younger than the minimum stop.</param>
/// <param name="DistanceMeters">Sum of great-circle legs, including the leg out of the preceding stop.</param>
/// <param name="DurationSeconds">From <paramref name="Departure"/> to <paramref name="End"/>.</param>
/// <param name="MovingSeconds">Time spent on legs faster than the moving threshold.</param>
/// <param name="MaxSpeedKmph">Highest receiver speed among the trip's fixes.</param>
/// <param name="HasGap">True when the trip bridges a data gap across which the car moved.</param>
/// <param name="PointCount">Number of fixes from the start to the end, inclusive.</param>
internal sealed record TripSegmentStats(
    DateTime Departure,
    DateTime End,
    TripEndKind EndKind,
    bool InProgress,
    double DistanceMeters,
    double DurationSeconds,
    double MovingSeconds,
    double MaxSpeedKmph,
    bool HasGap,
    int PointCount)
{
    private const double MetersPerSecondToKmph = 3.6;

    /// <summary>Distance over the whole duration in km/h, or null for a zero-length trip.</summary>
    public double? AverageSpeedKmph =>
        DurationSeconds > 0 ? DistanceMeters / DurationSeconds * MetersPerSecondToKmph : null;

    /// <summary>Distance over the moving time only in km/h, or null when the car never moved fast enough.</summary>
    public double? MovingAverageSpeedKmph =>
        MovingSeconds > 0 ? DistanceMeters / MovingSeconds * MetersPerSecondToKmph : null;
}
