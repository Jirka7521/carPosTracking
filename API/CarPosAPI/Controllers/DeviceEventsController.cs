using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// A device's history: when it went offline and why — a planned sleep, a sleep because
/// the car was parked, the power switch, a flat battery, a caught fault, or a connection
/// that simply died — the restarts worth knowing about, every wake from deep sleep and
/// what caused it, and every step of its motion-wake state machine.
///
/// <para>
/// Read-only, like <see cref="PositionsController"/> and for the same reason: events
/// arrive over MQTT, sealed by the device or published as its Last Will, and an HTTP
/// endpoint that could add one would be a way to forge "it was only asleep".
/// </para>
///
/// <para>
/// Its own controller under the device route rather than another action on
/// <see cref="DevicesController"/>, which already carries registration, provisioning
/// and settings — the same split <see cref="DeviceScheduleController"/> makes.
/// </para>
/// </summary>
[Route("api/devices/{deviceId}/events")]
[Authorize]
public sealed class DeviceEventsController : ApiControllerBase
{
    /// <summary>Default number of events returned when the caller does not ask.</summary>
    private const int DefaultLimit = 200;

    /// <summary>
    /// Hard ceiling on one page. A device that sleeps between reports logs several events
    /// per report, so a long range can hold many thousands; the caller narrows the range
    /// rather than asking for all of it.
    /// </summary>
    private const int MaxLimit = 1000;

    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDeviceEventQueryService _events;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="events">Runs the bounded, authorised query.</param>
    public DeviceEventsController(ICurrentUserAccessor currentUser, IDeviceEventQueryService events)
    {
        _currentUser = currentUser;
        _events = events;
    }

    /// <summary>Lists one device's events, newest first.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="from">Inclusive lower bound on the event's time (ISO 8601), optional.</param>
    /// <param name="to">Inclusive upper bound on the event's time (ISO 8601), optional.</param>
    /// <param name="minSeverity">
    /// <c>normal</c>, <c>alert</c> or <c>error</c>: only that severity and worse.
    /// Optional — omitted means every event.
    /// </param>
    /// <param name="limit">How many events to return; clamped to the endpoint's ceiling.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the events, 400 for an unknown severity, 404 when the device is not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DeviceEventDto>>> ListAsync(
        string deviceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery][RegularExpression("^(normal|alert|error)$")] string? minSeverity,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // Clamped rather than validated: a page size is a hint, and answering 400 to a
        // client that asked for a little too much helps nobody.
        int effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        OperationResult<IReadOnlyList<DeviceEventDto>> result = await _events.ListForDeviceAsync(
            userId,
            deviceId,
            from,
            to,
            minSeverity,
            effectiveLimit,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
