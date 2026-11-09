namespace CarPosAPI.Services.Privacy;

/// <summary>
/// A device's trip-detection thresholds as they appear in a data export, when
/// someone changed them from the defaults.
///
/// Exported for the same reason sampling profiles are: they shape what the system
/// derives from the device's positions.
/// </summary>
/// <param name="MinStopSeconds">How long the vehicle must stand still for a trip to end.</param>
/// <param name="StopRadiusMeters">How far the fixes may wander while it counts as standing still.</param>
/// <param name="MinTripDistanceMeters">Shorter trips are discarded.</param>
/// <param name="UpdatedAt">When the thresholds were last changed (UTC).</param>
public sealed record TripSettingsExportRow(
    int MinStopSeconds,
    int StopRadiusMeters,
    int MinTripDistanceMeters,
    DateTime UpdatedAt);
