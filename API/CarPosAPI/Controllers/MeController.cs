using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// Everything scoped to "the caller": their profile, the devices they can see,
/// and their private device nicknames.
///
/// These endpoints take no user id in the route on purpose. An id in the URL is
/// something a client can change, and every such parameter is one more place to
/// forget an ownership check; here the subject is always the token's own user.
///
/// <c>GET /api/me</c> doubles as the frontend's session probe: because the
/// session lives in an <c>HttpOnly</c> cookie, JavaScript cannot tell whether it
/// is signed in except by asking.
/// </summary>
[Route("api/me")]
[Authorize]
public sealed class MeController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IUserAccountService _accounts;
    private readonly IDeviceService _devices;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="accounts">Loads the caller's profile.</param>
    /// <param name="devices">Lists devices and writes aliases.</param>
    public MeController(
        ICurrentUserAccessor currentUser,
        IUserAccountService accounts,
        IDeviceService devices)
    {
        _currentUser = currentUser;
        _accounts = accounts;
        _devices = devices;
    }

    /// <summary>Returns the signed-in user's profile.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the profile, or 401 when there is no valid session.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserProfileDto>> GetProfileAsync(CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<UserProfileDto> result = await _accounts.GetProfileAsync(userId, cancellationToken);

        // A valid token for a user row that no longer exists. Not expected — users
        // are never deleted — but answering 404 for "who am I?" is clearer than
        // pretending the session is fine.
        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Lists every device the caller has access to.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the devices, including soft-deleted ones.</returns>
    [HttpGet("devices")]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // This is the filter that decides what the caller may see at all — no
        // client-side filtering is involved, and none would be trustworthy.
        IReadOnlyList<DeviceDto> devices = await _devices.ListForUserAsync(userId, cancellationToken);

        return Ok(devices);
    }

    /// <summary>Sets or clears the caller's private nickname for a device.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The new alias; empty clears it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 on success, 404 when the device is not visible to the caller.</returns>
    [HttpPut("devices/{deviceId}/alias")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetDeviceAliasAsync(
        string deviceId,
        [FromBody] DeviceAliasUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<bool> result =
            await _devices.SetAliasAsync(userId, deviceId, request.Alias, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }
}
