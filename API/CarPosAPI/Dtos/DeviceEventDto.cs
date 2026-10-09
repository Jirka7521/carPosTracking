namespace CarPosAPI.Dtos;

/// <summary>
/// One entry in a device's connection history, as the dashboard's Events tab shows it:
/// what happened, why, and how much it matters.
/// </summary>
/// <param name="Id">Row id — stable, increasing, usable as a list key.</param>
/// <param name="Kind">See <see cref="DeviceEventKindNames"/>.</param>
/// <param name="Reason">See <see cref="DeviceEventReasonNames"/>.</param>
/// <param name="Severity">See <see cref="DeviceEventSeverityNames"/>.</param>
/// <param name="ReceivedAt">
/// When the server received it (UTC). This is the event's time: a Last Will carries no
/// clock of its own, and the broker publishes it only once it has given up on the
/// session, so the arrival is the best answer there is for every kind.
/// </param>
/// <param name="DeviceTime">The device's own clock at the time (UTC), when it trusted one.</param>
/// <param name="BatteryPct">The battery percent the device last knew (0 = charging), if it said.</param>
/// <param name="SleepSeconds">How long the device expected to be away, if it said.</param>
/// <param name="Detail">A short machine code qualifying an error, e.g. <c>gnss_init</c>.</param>
public sealed record DeviceEventDto(
    long Id,
    string Kind,
    string Reason,
    string Severity,
    DateTime ReceivedAt,
    DateTime? DeviceTime,
    int? BatteryPct,
    int? SleepSeconds,
    string? Detail);
