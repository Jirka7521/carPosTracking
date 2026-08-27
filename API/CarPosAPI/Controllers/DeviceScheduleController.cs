using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// A device's settings schedule: the profiles it switches between, the weekly windows
/// that select them, and the manual override that can hold them off until the next
/// switch.
///
/// <para>
/// Its own controller rather than more actions on <see cref="DevicesController"/>,
/// which already carries registration, provisioning, key import and the settings
/// endpoints. The schedule is a resource with its own sub-collections, and nesting them
/// under a route prefix here is what keeps both controllers thin.
/// </para>
///
/// <para>
/// <b>Every action answers with the whole state.</b> Not a REST purity choice — a
/// practical one: adding a rule moves the next switch, retuning a profile can change
/// what is in force this second, and a client that had to re-fetch would always render
/// a stale answer in between. See <see cref="IDeviceConfigScheduleService"/>.
/// </para>
///
/// <para>
/// All times on this contract are <b>UTC minutes</b>. The API has no notion of a local
/// time and never converts one; the dashboard does, because it is the only party that
/// knows the reader's offset. The trade-off that comes with that is documented on
/// <see cref="SaveScheduleRuleRequestDto"/>.
/// </para>
/// </summary>
[Route("api/devices/{deviceId}/schedule")]
[Authorize]
public sealed class DeviceScheduleController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDeviceConfigScheduleService _schedule;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="schedule">Does the schedule work and authorises each call.</param>
    public DeviceScheduleController(
        ICurrentUserAccessor currentUser,
        IDeviceConfigScheduleService schedule)
    {
        _currentUser = currentUser;
        _schedule = schedule;
    }

    /// <summary>
    /// Returns the device's profiles, rules, what is in force right now, when it changes
    /// next, and any live override.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the state, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceScheduleStateDto>> GetAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.GetStateAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Turns the schedule on or off and sets the fallback profile used wherever no rule
    /// applies. Enabling applies the profile in force immediately.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The new schedule settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 400 when enabling without a usable fallback, 403, 404.</returns>
    [HttpPut]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceScheduleStateDto>> UpdateAsync(
        string deviceId,
        [FromBody] UpdateDeviceScheduleRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.UpdateSettingsAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Creates a profile — a named set of the seven settings.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">Name and values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 409 on a duplicate name or a full device, 403, 404.</returns>
    [HttpPost("profiles")]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceScheduleStateDto>> CreateProfileAsync(
        string deviceId,
        [FromBody] SaveConfigProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // 200 with the whole state rather than 201 with a location. There is no
        // GET .../profiles/{id} to name — the profile is only ever read as part of the
        // schedule — and a Created pointing at a URL that does not exist would be worse
        // than no location at all.
        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.CreateProfileAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Replaces a profile's name and values. If it is the profile currently in force the
    /// device is retuned immediately.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="profileId">The profile to replace.</param>
    /// <param name="request">Its new name and values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 409 on a duplicate name, 403, 404.</returns>
    [HttpPut("profiles/{profileId:guid}")]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceScheduleStateDto>> UpdateProfileAsync(
        string deviceId,
        Guid profileId,
        [FromBody] SaveConfigProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.UpdateProfileAsync(userId, deviceId, profileId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Deletes a profile that no rule and no fallback still points at.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="profileId">The profile to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 409 while it is still referenced, 403, 404.</returns>
    [HttpDelete("profiles/{profileId:guid}")]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceScheduleStateDto>> DeleteProfileAsync(
        string deviceId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // 200 with the state rather than 204, so a deletion that shifts what is in force
        // — it cannot here, but the client should not have to know that — leaves the
        // caller holding the current answer like every other action does.
        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.DeleteProfileAsync(userId, deviceId, profileId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Creates a weekly window that selects a profile.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The window, in UTC minutes.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 400 for a foreign profile, 409 when full, 403, 404.</returns>
    [HttpPost("rules")]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceScheduleStateDto>> CreateRuleAsync(
        string deviceId,
        [FromBody] SaveScheduleRuleRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.CreateRuleAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Replaces a rule's window, profile, priority and enabled flag.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule to replace.</param>
    /// <param name="request">Its new window.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 400 for a foreign profile, 403, 404.</returns>
    [HttpPut("rules/{ruleId:guid}")]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceScheduleStateDto>> UpdateRuleAsync(
        string deviceId,
        Guid ruleId,
        [FromBody] SaveScheduleRuleRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.UpdateRuleAsync(userId, deviceId, ruleId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Deletes a rule.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 403, 404.</returns>
    [HttpDelete("rules/{ruleId:guid}")]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceScheduleStateDto>> DeleteRuleAsync(
        string deviceId,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.DeleteRuleAsync(userId, deviceId, ruleId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Ends a manual override early and reapplies the profile the schedule says should
    /// be in force — the dashboard's "Resume schedule now".
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 400 when no schedule is enabled, 403, 404.</returns>
    [HttpPost("resume")]
    [ProducesResponseType(typeof(DeviceScheduleStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceScheduleStateDto>> ResumeAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceScheduleStateDto> result =
            await _schedule.ResumeAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
