using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Trips;

/// <summary>A device's journeys: the nested groups its trips are organised into.</summary>
public interface IJourneyService
{
    /// <summary>Every journey of the device with its trip counts.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The list, or why not.</returns>
    Task<OperationResult<JourneyListDto>> ListAsync(int userId, string deviceId, CancellationToken cancellationToken);

    /// <summary>Creates a journey.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">Name, parent, optional range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new journey, or why not.</returns>
    Task<OperationResult<JourneyDto>> CreateAsync(
        int userId,
        string deviceId,
        JourneySaveRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Replaces a journey's name, parent and range.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journeyId">The journey.</param>
    /// <param name="request">Name, parent, optional range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The saved journey, or why not.</returns>
    Task<OperationResult<JourneyDto>> UpdateAsync(
        int userId,
        string deviceId,
        Guid journeyId,
        JourneySaveRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Deletes a journey, moving its sub-journeys and trips up one level.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journeyId">The journey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Success, or why not.</returns>
    Task<OperationResult<bool>> DeleteAsync(int userId, string deviceId, Guid journeyId, CancellationToken cancellationToken);
}
