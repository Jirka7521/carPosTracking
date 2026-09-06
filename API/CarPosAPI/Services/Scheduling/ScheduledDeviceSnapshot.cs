namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// The few columns a reconciling pass needs from a device row.
///
/// <para>
/// A named record rather than an anonymous type (this project does not use <c>var</c>),
/// and a projection rather than the whole <c>Device</c> for the reason
/// <see cref="Devices.DeviceConfigPointers"/> gives: loading entities would pull every
/// device's protected private-key blob into memory on every tick, for ever, to read
/// four integers.
/// </para>
/// </summary>
/// <param name="RowId">Internal device id, for joins and for the revision writer.</param>
/// <param name="DeviceId">The MQTT identity, for log lines a human can act on.</param>
/// <param name="FallbackProfileId">The profile for time no rule covers.</param>
/// <param name="ScheduleBundleVersion">The bundle revision the server has published.</param>
/// <param name="ReportedScheduleVersion">The bundle revision the device last reported holding, or null.</param>
/// <param name="ReportedProfileSlot">The profile slot the device last reported running, or null.</param>
/// <param name="ReportedProfileAt">The fix time of that report, or null.</param>
internal sealed record ScheduledDeviceSnapshot(
    Guid RowId,
    string DeviceId,
    Guid? FallbackProfileId,
    int ScheduleBundleVersion,
    int? ReportedScheduleVersion,
    int? ReportedProfileSlot,
    DateTime? ReportedProfileAt);
