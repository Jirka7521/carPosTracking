using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Positions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// Read access to stored GNSS fixes.
///
/// There is deliberately no POST, PUT or DELETE. Positions arrive over MQTT,
/// end-to-end encrypted, and are written by the ingest pipeline alone; an HTTP
/// endpoint that could add or edit one would be a way to forge history without
/// holding a device's key.
///
/// <c>deviceId</c> is required rather than optional: the table grows without
/// bound, and "all positions" is a query that would eventually take the API down
/// on its own.
/// </summary>
[Route("api/positions")]
[Authorize]
public sealed class PositionsController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IPositionQueryService _positions;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="positions">Runs the bounded, authorised query.</param>
    public PositionsController(ICurrentUserAccessor currentUser, IPositionQueryService positions)
    {
        _currentUser = currentUser;
        _positions = positions;
    }

    /// <summary>Lists one device's fixes, newest first, capped at 1000.</summary>
    /// <param name="deviceId">The device's MQTT identity. Required.</param>
    /// <param name="from">Inclusive lower bound on fix time (ISO 8601), optional.</param>
    /// <param name="to">Inclusive upper bound on fix time (ISO 8601), optional.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the fixes, or 404 when the device is not visible to the caller.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PositionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PositionDto>>> ListAsync(
        [FromQuery][Required][StringLength(64, MinimumLength = 1)] string deviceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<IReadOnlyList<PositionDto>> result =
            await _positions.ListForDeviceAsync(userId, deviceId, from, to, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
