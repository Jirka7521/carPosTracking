using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Reads and changes a device's remote settings, and keeps the broker's retained copy
/// in step with the database.
///
/// Separate from <see cref="IDeviceService"/> because it owns a different resource with
/// a different lifecycle: devices are created once and retired once, whereas settings
/// accumulate an unbounded, immutable history that this service is the only writer of.
/// </summary>
public interface IDeviceConfigService
{
    /// <summary>
    /// Returns what the device should be running and what it last confirmed it is
    /// running, both with full values.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The state, 403 without <c>CanModifySettings</c>, or 404 when not visible.</returns>
    Task<OperationResult<DeviceConfigStateDto>> GetStateAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);

    /// <summary>Returns the most recent revisions, newest first.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="limit">How many revisions to return, already bounded by the controller.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The history, 403 without <c>CanModifySettings</c>, or 404 when not visible.</returns>
    Task<OperationResult<IReadOnlyList<DeviceConfigVersionDto>>> GetHistoryAsync(
        int userId,
        string deviceId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a new revision and publishes it retained. Saving values identical to the
    /// current revision is a no-op that returns the existing state, so a double-click
    /// cannot inflate the history.
    /// </summary>
    /// <param name="userId">The caller, recorded as the revision's author.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The complete new settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 403 without <c>CanModifySettings</c>, or 404 when not visible.</returns>
    Task<OperationResult<DeviceConfigStateDto>> UpdateAsync(
        int userId,
        string deviceId,
        UpdateDeviceConfigRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-publishes the current revision without creating a new one — the "the device
    /// never picked it up, send it again" button.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>True when the broker took it, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    Task<OperationResult<bool>> RepublishAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);
}
