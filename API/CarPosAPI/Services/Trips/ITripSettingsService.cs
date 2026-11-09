using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Trips;

/// <summary>A device's trip detection thresholds (the Expert "Trip detection" section).</summary>
public interface ITripSettingsService
{
    /// <summary>The device's thresholds, or the defaults.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The thresholds, or why not.</returns>
    Task<OperationResult<TripSettingsDto>> GetAsync(int userId, string deviceId, CancellationToken cancellationToken);

    /// <summary>Saves new thresholds and, if they changed, recomputes every trip of the device.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The thresholds.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The saved thresholds, or why not.</returns>
    Task<OperationResult<TripSettingsDto>> UpdateAsync(
        int userId,
        string deviceId,
        TripSettingsUpdateRequestDto request,
        CancellationToken cancellationToken);
}
