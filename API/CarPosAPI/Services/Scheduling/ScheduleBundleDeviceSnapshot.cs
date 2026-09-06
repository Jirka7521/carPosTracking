namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// The device columns <see cref="ScheduleBundleBuilder"/> needs, and nothing else.
///
/// A named record because both of the builder's loading paths project to it inside a
/// LINQ query, and this project does not use anonymous types. Keeping it to these seven
/// columns is also what stops the fleet-wide sweep dragging every device's key material
/// into memory on each broker reconnect.
/// </summary>
/// <param name="RowId">The device's internal row id.</param>
/// <param name="DeviceId">The device's MQTT identity, e.g. <c>GNSS01</c>.</param>
/// <param name="ScheduleBundleVersion">Revision of the bundle being built.</param>
/// <param name="ScheduleEnabled">Whether device-side switching is turned on.</param>
/// <param name="FallbackProfileId">Profile used where no window covers the instant, or null.</param>
/// <param name="OverrideUntil">When a manual or corrective override lapses, or null.</param>
/// <param name="ConfigVersion">The revision currently in force - the override's values.</param>
internal sealed record ScheduleBundleDeviceSnapshot(
    Guid RowId,
    string DeviceId,
    int ScheduleBundleVersion,
    bool ScheduleEnabled,
    Guid? FallbackProfileId,
    DateTime? OverrideUntil,
    int ConfigVersion);
