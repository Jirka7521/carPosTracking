using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Trips;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// A device's trips for the dashboard's Trips tab: the list, one trip, a date range's
/// preview, and the user's edits (names and manual journey placement).
///
/// <para>
/// Trips are detected by the server from the device's positions; only their starts are
/// stored, and each response rebuilds ends and statistics from the positions. Reading
/// needs any grant on the device; organising needs <c>CanModifySettings</c>.
/// </para>
/// </summary>
[Route("api/devices/{deviceId}/trips")]
[Authorize]
public sealed class DeviceTripsController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ITripReadService _reads;
    private readonly ITripAssignmentService _assignments;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="reads">Lists and rebuilds trips.</param>
    /// <param name="assignments">Names and places trips.</param>
    public DeviceTripsController(
        ICurrentUserAccessor currentUser,
        ITripReadService reads,
        ITripAssignmentService assignments)
    {
        _currentUser = currentUser;
        _reads = reads;
        _assignments = assignments;
    }

    /// <summary>Lists one device's trips, newest first.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journey"><c>all</c> (default), <c>none</c>, or a journey id for the trips directly in it.</param>
    /// <param name="before">The <c>nextCursor</c> of the previous page.</param>
    /// <param name="limit">Page size; clamped.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the page; 404 when the device or journey is not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(TripPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripPageDto>> ListAsync(
        string deviceId,
        [FromQuery][RegularExpression("^(all|none|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$")] string? journey,
        [FromQuery] DateTime? before,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // Clamped rather than validated: a page size is a hint.
        int effectiveLimit = Math.Clamp(limit ?? TripRules.DefaultPageSize, 1, TripRules.MaxPageSize);

        OperationResult<TripPageDto> result = await _reads.ListAsync(
            userId,
            deviceId,
            journey,
            before,
            effectiveLimit,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>What a date range holds — for the journey dialog's preview.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="from">Inclusive start (ISO 8601).</param>
    /// <param name="to">Exclusive end (ISO 8601).</param>
    /// <param name="journeyId">The journey being edited, if any.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the summary; 400 for a backwards range; 404 when the device is not visible.</returns>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(TripRangeSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripRangeSummaryDto>> SummarizeAsync(
        string deviceId,
        [FromQuery][Required] DateTime? from,
        [FromQuery][Required] DateTime? to,
        [FromQuery] Guid? journeyId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<TripRangeSummaryDto> result = await _reads.SummarizeRangeAsync(
            userId,
            deviceId,
            from!.Value,
            to!.Value,
            journeyId,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>One trip, with its end and statistics.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="tripId">The trip.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the trip; 404 when the device or trip is not visible.</returns>
    [HttpGet("{tripId:long}")]
    [ProducesResponseType(typeof(TripSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripSummaryDto>> GetAsync(string deviceId, long tripId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<TripSummaryDto> result = await _reads.GetAsync(userId, deviceId, tripId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Names a trip (a blank name clears it).</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="tripId">The trip.</param>
    /// <param name="request">The new name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204; 403 without the settings right; 404 when the device or trip is not visible.</returns>
    [HttpPut("{tripId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameAsync(
        string deviceId,
        long tripId,
        [FromBody] TripRenameRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<bool> result = await _assignments.RenameAsync(userId, deviceId, tripId, request.Name, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }

    /// <summary>Pins trips to a journey (or to none), or hands them back to the date ranges.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">Which trips, and where.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with how many moved; 403 without the settings right; 404 when the device or journey is not visible.</returns>
    [HttpPost("assign")]
    [ProducesResponseType(typeof(TripAssignResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TripAssignResultDto>> AssignAsync(
        string deviceId,
        [FromBody] TripAssignRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<TripAssignResultDto> result = await _assignments.AssignAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
