using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Trips;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// A device's journeys — the nested groups ("Holidays", "Croatia 2026") its trips are
/// organised into. Journeys belong to the car, so everyone with access sees the same
/// ones; changing them needs <c>CanModifySettings</c>.
/// </summary>
[Route("api/devices/{deviceId}/journeys")]
[Authorize]
public sealed class DeviceJourneysController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IJourneyService _journeys;
    private readonly ITripReadService _reads;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="journeys">Lists and edits journeys.</param>
    /// <param name="reads">Computes a journey's totals.</param>
    public DeviceJourneysController(ICurrentUserAccessor currentUser, IJourneyService journeys, ITripReadService reads)
    {
        _currentUser = currentUser;
        _journeys = journeys;
        _reads = reads;
    }

    /// <summary>Every journey of the device, flat, with trip counts.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the list; 404 when the device is not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(JourneyListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JourneyListDto>> ListAsync(string deviceId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<JourneyListDto> result = await _journeys.ListAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>A journey's totals, including its sub-journeys, computed from the positions.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journeyId">The journey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the totals; 404 when the device or journey is not visible.</returns>
    [HttpGet("{journeyId:guid}/summary")]
    [ProducesResponseType(typeof(JourneySummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JourneySummaryDto>> SummarizeAsync(
        string deviceId,
        Guid journeyId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<JourneySummaryDto> result = await _reads.SummarizeJourneyAsync(userId, deviceId, journeyId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Creates a journey, optionally with a live date range.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">Name, parent and range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the journey; 400/409 when a rule refuses it; 403 without the settings right.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(JourneyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JourneyDto>> CreateAsync(
        string deviceId,
        [FromBody] JourneySaveRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<JourneyDto> result = await _journeys.CreateAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Replaces a journey's name, parent and range.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journeyId">The journey.</param>
    /// <param name="request">Name, parent and range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the journey; 400/409 when a rule refuses it; 403 without the settings right; 404 when not visible.</returns>
    [HttpPut("{journeyId:guid}")]
    [ProducesResponseType(typeof(JourneyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<JourneyDto>> UpdateAsync(
        string deviceId,
        Guid journeyId,
        [FromBody] JourneySaveRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<JourneyDto> result = await _journeys.UpdateAsync(userId, deviceId, journeyId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Deletes a journey; its sub-journeys and trips move up one level.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journeyId">The journey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 without the settings right; 404 when not visible; 409 on a name clash one level up.</returns>
    [HttpDelete("{journeyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(string deviceId, Guid journeyId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<bool> result = await _journeys.DeleteAsync(userId, deviceId, journeyId, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }
}
