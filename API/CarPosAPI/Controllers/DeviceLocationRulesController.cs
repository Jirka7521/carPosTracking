using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Trips;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// A device's location rules — circles on the map whose trips (starting or ending there)
/// join a chosen journey automatically, from a valid-from date until the rule is ended.
/// Ended rules stay as history; there is no delete. Reading needs any grant on the
/// device; creating, editing and ending need <c>CanModifySettings</c>.
/// </summary>
[Route("api/devices/{deviceId}/location-rules")]
[Authorize]
public sealed class DeviceLocationRulesController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ILocationRuleService _rules;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="rules">Lists, previews and edits location rules.</param>
    public DeviceLocationRulesController(ICurrentUserAccessor currentUser, ILocationRuleService rules)
    {
        _currentUser = currentUser;
        _rules = rules;
    }

    /// <summary>Every rule of the device — in force first, then the ended ones — with trip counts.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the rules; 404 when the device is not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LocationRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<LocationRuleDto>>> ListAsync(string deviceId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<IReadOnlyList<LocationRuleDto>> result = await _rules.ListAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>What a rule drawn so far would catch — for the rule dialog's live preview.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="latitude">Centre latitude.</param>
    /// <param name="longitude">Centre longitude.</param>
    /// <param name="radiusMeters">Radius in metres.</param>
    /// <param name="matchStart">Count trips starting in the circle.</param>
    /// <param name="matchEnd">Count trips ending in the circle.</param>
    /// <param name="validFrom">Only trips starting at or after this instant (ISO 8601).</param>
    /// <param name="excludeRuleId">The rule being edited, if any.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with counts and points; 400 for a bad circle or no kind of match; 404 when the device is not visible.</returns>
    [HttpGet("preview")]
    [ProducesResponseType(typeof(LocationRulePreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LocationRulePreviewDto>> PreviewAsync(
        string deviceId,
        [FromQuery][Required][Range(-90.0, 90.0)] double? latitude,
        [FromQuery][Required][Range(-180.0, 180.0)] double? longitude,
        [FromQuery][Required][Range(TripRules.MinLocationRuleRadiusMeters, TripRules.MaxLocationRuleRadiusMeters)] int? radiusMeters,
        [FromQuery] bool matchStart,
        [FromQuery] bool matchEnd,
        [FromQuery][Required] DateTime? validFrom,
        [FromQuery] Guid? excludeRuleId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        LocationRulePreviewQuery query = new LocationRulePreviewQuery(
            latitude!.Value,
            longitude!.Value,
            radiusMeters!.Value,
            matchStart,
            matchEnd,
            validFrom!.Value,
            excludeRuleId);

        OperationResult<LocationRulePreviewDto> result = await _rules.PreviewAsync(userId, deviceId, query, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Creates a rule; it applies at once, to past trips since its valid-from date too.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The rule.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the rule; 400 when invalid; 403 without the settings right; 404 when the device or journey is not visible; 409 at the cap.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(LocationRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LocationRuleDto>> CreateAsync(
        string deviceId,
        [FromBody] LocationRuleSaveRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<LocationRuleDto> result = await _rules.CreateAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Replaces a rule that is still in force.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="request">The rule's new values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the rule; 400 when invalid; 403 without the settings right; 404 when not visible; 409 when it has ended.</returns>
    [HttpPut("{ruleId:guid}")]
    [ProducesResponseType(typeof(LocationRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LocationRuleDto>> UpdateAsync(
        string deviceId,
        Guid ruleId,
        [FromBody] LocationRuleSaveRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<LocationRuleDto> result = await _rules.UpdateAsync(userId, deviceId, ruleId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Ends a rule now. The trips it placed so far stay in the journey, and the rule stays as history.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the ended rule; 403 without the settings right; 404 when not visible; 409 when it has already ended.</returns>
    [HttpPost("{ruleId:guid}/end")]
    [ProducesResponseType(typeof(LocationRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LocationRuleDto>> EndAsync(string deviceId, Guid ruleId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<LocationRuleDto> result = await _rules.EndAsync(userId, deviceId, ruleId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
