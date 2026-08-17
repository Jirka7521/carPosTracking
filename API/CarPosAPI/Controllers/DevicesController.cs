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
    /// <summary>Default number of configuration revisions returned by the history endpoint.</summary>
    private const int DefaultConfigHistoryLimit = 20;

    /// <summary>
    /// Hard ceiling on the history page size. Configuration history is unbounded — a
    /// device retuned daily for years has thousands of rows — so the caller may ask for
    /// more than the default but never for all of it.
    /// </summary>
    private const int MaxConfigHistoryLimit = 100;

    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDeviceService _devices;
    private readonly IDeviceConfigService _deviceConfig;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="devices">Does the device work and authorises each call.</param>
    /// <param name="deviceConfig">Reads and changes remote settings, and publishes them.</param>
    public DevicesController(
        ICurrentUserAccessor currentUser,
        IDeviceService devices,
        IDeviceConfigService deviceConfig)
    {
        _currentUser = currentUser;
        _devices = devices;
        _deviceConfig = deviceConfig;
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

    /// <summary>
    /// Stores a newly generated ack <em>public</em> key for the device, replacing any
    /// previous one, and returns its fingerprint.
    /// </summary>
    /// <remarks>
    /// The private half is generated in the operator's browser and woven into the
    /// firmware config file there, so it never reaches this API — which is what allows
    /// a key that the server encrypts to be rotated from a dashboard at all. The
    /// service refuses a body carrying private-key material regardless.
    ///
    /// <para>
    /// Callers must not reach this until the operator has saved the config file
    /// containing the matching private key: from the moment it is stored the API seals
    /// every ack to it, and a device still running the old key simply stops confirming
    /// deliveries. The dashboard gates the call behind an explicit confirmation for
    /// that reason.
    /// </para>
    /// </remarks>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The candidate RSA-3072 public key, SPKI PEM.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the fingerprint, 400 if the key is unusable, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    [HttpPost("{deviceId}/ack-key")]
    [ProducesResponseType(typeof(AckKeyImportedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AckKeyImportedDto>> ImportAckKeyAsync(
        string deviceId,
        [FromBody] ImportAckKeyRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<AckKeyImportedDto> result =
            await _devices.ImportAckKeyAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Returns the device's remote settings: the revision published to it, the revision
    /// it last confirmed it is running, and whether those agree.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the state, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    [HttpGet("{deviceId}/config")]
    [ProducesResponseType(typeof(DeviceConfigStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceConfigStateDto>> GetConfigAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<DeviceConfigStateDto> result =
            await _deviceConfig.GetStateAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Returns the device's configuration history, newest revision first.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="limit">How many revisions to return; clamped to the endpoint's ceiling.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the revisions, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    [HttpGet("{deviceId}/config/history")]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceConfigVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DeviceConfigVersionDto>>> GetConfigHistoryAsync(
        string deviceId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // Clamped rather than validated: a page size is a hint, and answering 400
        // because someone asked for 500 rows would be pedantic. An out-of-range value
        // that reached the service, on the other hand, would be an unbounded query.
        int effectiveLimit = Math.Clamp(
            limit ?? DefaultConfigHistoryLimit,
            1,
            MaxConfigHistoryLimit);

        OperationResult<IReadOnlyList<DeviceConfigVersionDto>> result =
            await _deviceConfig.GetHistoryAsync(userId, deviceId, effectiveLimit, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Replaces the device's settings, creating a new revision and publishing it to the
    /// broker retained. Submitting the values already in force changes nothing and adds
    /// no revision.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The complete new settings — a replacement, not a patch.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new state, 400 out of range, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    [HttpPut("{deviceId}/config")]
    [ProducesResponseType(typeof(DeviceConfigStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceConfigStateDto>> UpdateConfigAsync(
        string deviceId,
        [FromBody] UpdateDeviceConfigRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // [ApiController] has already rejected out-of-range values with a 400
        // ValidationProblemDetails built from the [Range] attributes, so everything
        // arriving here is within the same bounds the firmware would clamp to.
        OperationResult<DeviceConfigStateDto> result =
            await _deviceConfig.UpdateAsync(userId, deviceId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Re-publishes the device's current settings without creating a revision — for
    /// when a device has not picked a change up and the operator wants to be sure the
    /// broker is still holding it.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 on success, 403 without <c>CanModifySettings</c>, 404 when not visible.</returns>
    [HttpPost("{deviceId}/config/republish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RepublishConfigAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<bool> result =
            await _deviceConfig.RepublishAsync(userId, deviceId, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        // The service reports "the broker would not take it" as a successful call with
        // a false value, because nothing about the stored settings changed. It is still
        // not what the operator asked for, so it must not be dressed up as a 204.
        return result.Value
            ? NoContent()
            : Problem(
                title: "Broker unavailable",
                detail: "The settings are saved, but the broker could not be reached to publish them. "
                    + "They will be sent automatically once the connection is restored.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
