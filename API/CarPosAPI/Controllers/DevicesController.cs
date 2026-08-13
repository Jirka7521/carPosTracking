using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// The device resource: registering a tracker, retiring it, and recovering the
/// firmware configuration block it was provisioned with.
///
/// Listing devices lives on <see cref="MeController"/> instead
/// (<c>GET /api/me/devices</c>), because "all devices" is not something any caller
/// is entitled to ask for — only "the devices I can see".
///
/// Registration is the sharpest edge in the API: it creates a device the MQTT
/// ingest will trust, and generates the RSA key pair the end-to-end encryption
/// depends on. It used to be reachable in Development only for want of anything to
/// guard it with; the <c>[Authorize]</c> attribute below is now that guard.
/// </summary>
[Route("api/devices")]
[Authorize]
public sealed class DevicesController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDeviceService _devices;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="devices">Does the device work and authorises each call.</param>
    public DevicesController(ICurrentUserAccessor currentUser, IDeviceService devices)
    {
        _currentUser = currentUser;
        _devices = devices;
    }

    /// <summary>
    /// Registers a device: generates its RSA-3072 key pair, stores the private half
    /// encrypted at rest, grants the caller full access, optionally shares it with
    /// others, and returns the public half plus a paste-ready <c>Config.h</c> block.
    /// </summary>
    /// <param name="request">Device id, optional display name, optional co-owners.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the device and its provisioning payload, or 409 if the id is taken.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(DeviceCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceCreatedDto>> CreateAsync(
        [FromBody] CreateDeviceRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // [ApiController] already answered malformed bodies with a 400
        // ValidationProblemDetails, so by here the device id is known-good — and
        // known to contain no MQTT topic separator or wildcard.
        OperationResult<DeviceCreatedDto> result = await _devices.CreateAsync(userId, request, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        // A literal location rather than CreatedAtAction: there is no
        // GET /api/devices/{id} to name, and CreatedAtAction throws when it cannot
        // resolve a route. This URL is the one such an endpoint would occupy.
        return Created($"/api/devices/{result.Value!.Device.DeviceId}", result.Value);
    }

    /// <summary>Soft-deletes (deactivates) a device.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 on success, 403 without <c>CanDelete</c>, 404 when not visible.</returns>
    [HttpDelete("{deviceId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(string deviceId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<bool> result = await _devices.DeactivateAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }

    /// <summary>
    /// Re-renders the device's firmware configuration block — topics, broker URI,
    /// public key, fingerprint and the C++ snippet.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the payload, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    [HttpGet("{deviceId}/provisioning")]
    [ProducesResponseType(typeof(DeviceProvisioningResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceProvisioningResultDto>> GetProvisioningAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // Returns the *public* key only. The private half is encrypted at rest and
        // has no code path out of the database — not here, not anywhere.
        OperationResult<DeviceProvisioningResultDto> result =
            await _devices.GetProvisioningAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }
}
