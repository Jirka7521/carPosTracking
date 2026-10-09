using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Reads a device's connection history back out for the dashboard's Events tab.
/// Read-only by design — events are written exclusively by the status ingest
/// (<see cref="Ingest.DeviceStatusWriter"/>) from the device's own sealed messages, and
/// nothing in the HTTP surface may create or edit one: that would be a way to forge
/// "the tracker was asleep" without holding its key.
/// </summary>
public interface IDeviceEventQueryService
{
    /// <summary>Loads one device's events, newest first.</summary>
    /// <param name="userId">The authenticated caller; must hold a grant on the device.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="fromUtc">Inclusive lower bound on receive time, or null.</param>
    /// <param name="toUtc">Inclusive upper bound on receive time, or null.</param>
    /// <param name="minSeverity">
    /// Only this severity and worse (<see cref="DeviceEventSeverityNames"/>), or null
    /// for every event.
    /// </param>
    /// <param name="limit">Most rows to return — already clamped by the caller.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The events, or <see cref="OperationOutcome.NotFound"/> when the device is not visible.</returns>
    Task<OperationResult<IReadOnlyList<DeviceEventDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? minSeverity,
        int limit,
        CancellationToken cancellationToken);
}
