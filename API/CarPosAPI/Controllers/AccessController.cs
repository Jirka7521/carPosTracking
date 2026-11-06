using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// Sharing grants — who else may see a device, and how much they may do with it.
///
/// Every action here requires <c>CanShare</c> on the device concerned, resolved
/// server-side from the caller's own grant. The grant id in the route is never
/// taken as evidence of anything: it identifies the row, it does not authorise
/// touching it.
///
/// <para>
/// There is deliberately no action that <em>creates</em> a grant. It used to take
/// any user id and grant that account access on the spot, without asking. Because
/// sharing a device is what lets two accounts see each other's profile, that turned
/// an integer scan into a dump of every user's name and email. A grant now only
/// comes into being when its recipient accepts an invitation
/// (<see cref="AccessInvitationsController"/>).
/// </para>
/// </summary>
[Route("api/access")]
[Authorize]
public sealed class AccessController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAccessService _access;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="access">Does the sharing work and authorises each call.</param>
    public AccessController(ICurrentUserAccessor currentUser, IAccessService access)
    {
        _currentUser = currentUser;
        _access = access;
    }

    /// <summary>Lists the active grants on a device.</summary>
    /// <param name="deviceId">The device's MQTT identity. Required.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the grants, 403 without <c>CanShare</c>, 404 when not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AccessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AccessDto>>> ListAsync(
        [FromQuery][Required][StringLength(64, MinimumLength = 1)] string deviceId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<IReadOnlyList<AccessDto>> result =
            await _access.ListForDeviceAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Replaces the capability set on an existing grant.</summary>
    /// <param name="accessId">The grant to change.</param>
    /// <param name="request">The new capability set — a full replacement, not a patch.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the updated grant, or the reason it was refused.</returns>
    [HttpPut("{accessId:int}")]
    [ProducesResponseType(typeof(AccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccessDto>> UpdateAsync(
        int accessId,
        [FromBody] AccessUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<AccessDto> result = await _access.UpdateAsync(userId, accessId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Revokes a grant. The row is deactivated, never deleted.</summary>
    /// <param name="accessId">The grant to revoke.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 on success, or the reason it was refused.</returns>
    [HttpDelete("{accessId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(int accessId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<bool> result = await _access.RevokeAsync(userId, accessId, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }
}
