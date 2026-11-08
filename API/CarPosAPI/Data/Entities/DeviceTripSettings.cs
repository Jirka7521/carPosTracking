namespace CarPosAPI.Data.Entities;

/// <summary>
/// A device's trip detection thresholds, edited in the dashboard's Expert settings.
///
/// <para>
/// Server-side only: unlike <see cref="DeviceConfigVersion"/> these values are never
/// sent to the tracker — they only change how the API cuts its stored fixes into
/// trips. A device without a row uses the defaults in <see cref="Dtos.TripRules"/>,
/// so the table only holds cars whose owner changed something.
/// </para>
/// Mapped by <see cref="Configurations.DeviceTripSettingsConfiguration"/>.
/// </summary>
public sealed class DeviceTripSettings
{
    /// <summary>The device these thresholds apply to (PK and FK).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Shortest stop, in seconds, that ends a trip.</summary>
    public int MinStopSeconds { get; set; }

    /// <summary>How far, in metres, the car may wander while still counting as stopped.</summary>
    public int StopRadiusMeters { get; set; }

    /// <summary>Movements shorter than this many metres are not trips.</summary>
    public int MinTripDistanceMeters { get; set; }

    /// <summary>When the thresholds were last saved (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
