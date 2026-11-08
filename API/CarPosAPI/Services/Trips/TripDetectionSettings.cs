using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// The user-tunable thresholds the trip logic runs with — the plain-record view of
/// <see cref="Data.Entities.DeviceTripSettings"/>, or <see cref="Default"/> for a device
/// that has none.
/// </summary>
/// <param name="MinStopSeconds">Shortest stop, in seconds, that ends a trip.</param>
/// <param name="StopRadiusMeters">How far the car may wander while still counting as stopped.</param>
/// <param name="MinTripDistanceMeters">Movements shorter than this are not trips.</param>
internal sealed record TripDetectionSettings(int MinStopSeconds, int StopRadiusMeters, int MinTripDistanceMeters)
{
    /// <summary>The thresholds of a device whose owner never changed them.</summary>
    public static TripDetectionSettings Default { get; } = new TripDetectionSettings(
        TripRules.DefaultMinStopSeconds,
        TripRules.DefaultStopRadiusMeters,
        TripRules.DefaultMinTripDistanceMeters);
}
