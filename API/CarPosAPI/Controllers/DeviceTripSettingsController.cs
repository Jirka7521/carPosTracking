using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Trips;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// A device's trip detection thresholds — the dashboard's Expert "Trip detection"
/// section. Server-side only: unlike <c>PUT /api/devices/{deviceId}/config</c> nothing is
/// sent to the tracker; a change recomputes the device's trips.
/// </summary>
[Route("api/devices/{deviceId}/trip-settings")]
[Authorize]
public sealed class DeviceTripSettingsController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ITripSettingsService _settings;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="settings">Reads and saves the thresholds.</param>
    public DeviceTripSettingsController(ICurrentUserAccessor currentUser, ITripSettingsService settings)
    {
        _currentUser = currentUser;
        _settings = settings;
    }

    /// <summary>The device's thresholds, or the defaults.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the thresholds; 404 when the device is not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(TripSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripSettingsDto>> GetAsync(string deviceId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<TripSettingsDto> result = await _settings.GetAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Saves the thresholds; changed values recompute every trip of the device.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The thresholds.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the saved thresholds; 400 out of range; 403 without the settings right; 404 when not visible.</returns>
    [HttpPut]
    [ProducesResponseType(typeof(TripSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripSettingsDto>> UpdateAsync(
        string deviceId,
        [FromBody] TripSettingsUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<TripSettingsDto> result = await _settings.UpdateAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
