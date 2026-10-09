namespace CarPosAPI.Dtos;

/// <summary>
/// One entry in a device's history, as the dashboard's Events tab shows it: what
/// happened, why, and how much it matters.
/// </summary>
/// <param name="Id">Row id — stable, increasing, usable as a list key.</param>
/// <param name="Kind">See <see cref="DeviceEventKindNames"/>.</param>
/// <param name="Reason">See <see cref="DeviceEventReasonNames"/>.</param>
/// <param name="Severity">See <see cref="DeviceEventSeverityNames"/>.</param>
/// <param name="OccurredAt">
/// When it happened (UTC) — the event's time, which the list is ordered by. The receive
/// time, unless the device kept the message on its SD card while out of range; then
/// the device's clock (see <c>Data.Entities.DeviceEvent.OccurredAt</c>).
/// </param>
/// <param name="ReceivedAt">
/// When the server received it (UTC). Equal to <paramref name="OccurredAt"/> for a live
/// message; later for one that waited on the card.
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
    DateTime OccurredAt,
    DateTime ReceivedAt,
    DateTime? DeviceTime,
    int? BatteryPct,
    int? SleepSeconds,
    string? Detail);
