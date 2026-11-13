using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Answers "how often should this device be reporting right now", for the map's
/// overdue pin.
///
/// <para>
/// Separate from <see cref="IDeviceConfigService"/> because it has a different
/// audience: settings are for <c>CanModifySettings</c>, but whether the latest
/// position is current is a question every viewer of the map asks — including a share
/// visitor, who never sees a setting at all.
/// </para>
/// </summary>
public interface IDeviceReportIntervalService
{
    /// <summary>Returns the device's worst-case reporting interval and overdue threshold.</summary>
    /// <param name="userId">The caller; any active grant may ask.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The interval, or 404 when the device is not visible.</returns>
    Task<OperationResult<DeviceReportIntervalDto>> GetAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// How old a device's latest fix may get before it is overdue. For callers that
    /// have already authorised the device by other means, such as a share link.
    /// </summary>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Seconds, or null when the device is retired or has no stored settings.</returns>
    Task<int?> GetOverdueAfterSecondsAsync(
        Guid deviceRowId,
        CancellationToken cancellationToken);
}
