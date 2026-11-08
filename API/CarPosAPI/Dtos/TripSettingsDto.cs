namespace CarPosAPI.Dtos;

/// <summary>A device's trip detection thresholds (<c>GET /api/devices/{deviceId}/trip-settings</c>).</summary>
/// <param name="MinStopSeconds">Shortest stop that ends a trip.</param>
/// <param name="StopRadiusMeters">How far the car may wander while still counting as stopped.</param>
/// <param name="MinTripDistanceMeters">Movements shorter than this are not trips.</param>
/// <param name="IsDefault">True when the device has never had its thresholds changed.</param>
public sealed record TripSettingsDto(int MinStopSeconds, int StopRadiusMeters, int MinTripDistanceMeters, bool IsDefault);
