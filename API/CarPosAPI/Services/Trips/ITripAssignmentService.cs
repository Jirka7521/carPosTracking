using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Trips;

/// <summary>The user's edits to individual trips: names and manual journey placement.</summary>
public interface ITripAssignmentService
{
    /// <summary>Names a trip, or clears its name.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="tripId">The trip.</param>
    /// <param name="name">The new name; blank clears it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Success, or why not.</returns>
    Task<OperationResult<bool>> RenameAsync(int userId, string deviceId, long tripId, string name, CancellationToken cancellationToken);

    /// <summary>Pins trips to a journey (or to none), or hands them back to the date ranges.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">Which trips, and where.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>How many trips moved, or why not.</returns>
    Task<OperationResult<TripAssignResultDto>> AssignAsync(
        int userId,
        string deviceId,
        TripAssignRequestDto request,
        CancellationToken cancellationToken);
}
