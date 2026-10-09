namespace CarPosAPI.Dtos;

/// <summary>
/// The latest time a device went offline, and why — carried on every
/// <see cref="DeviceDto"/> so the device grid can show "sleeping, back about 14:05" or
/// "connection lost" without loading the event history.
///
/// Only the four fields the badge needs. Whether the device has since come back is
/// answered by comparing <see cref="OccurredAt"/> against
/// <see cref="DeviceDto.LastOnlineAt"/> and <see cref="DeviceDto.LastSeenAt"/>.
/// </summary>
/// <param name="Reason">See <see cref="DeviceEventReasonNames"/>.</param>
/// <param name="Severity">See <see cref="DeviceEventSeverityNames"/>.</param>
/// <param name="OccurredAt">
/// When it went offline (UTC). The server's receive time for a live goodbye; the
/// device's clock for one said out of range that reached the server later.
/// </param>
/// <param name="SleepSeconds">How long it expected to be away, when it said.</param>
public sealed record DeviceOfflineEventDto(
    string Reason,
    string Severity,
    DateTime OccurredAt,
    int? SleepSeconds);
