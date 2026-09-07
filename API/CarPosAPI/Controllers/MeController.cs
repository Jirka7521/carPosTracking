using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Devices;
using CarPosAPI.Services.Privacy;
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
    /// <summary>
    /// Filename stem of the export download. The user id and a UTC stamp are
    /// appended, so two exports never land on top of each other in a downloads
    /// folder.
    /// </summary>
    private const string ExportFileNameStem = "carpos-export";

    private readonly ICurrentUserAccessor _currentUser;
    private readonly IUserAccountService _accounts;
    private readonly IDeviceService _devices;
    private readonly IDataExportService _dataExport;
    private readonly IAccountErasureService _erasure;
    private readonly ISessionCookieWriter _sessionCookies;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="accounts">Loads the caller's profile.</param>
    /// <param name="devices">Lists devices and writes aliases.</param>
    /// <param name="dataExport">Streams the GDPR Art. 15/20 export.</param>
    /// <param name="erasure">Performs GDPR Art. 17 account erasure.</param>
    /// <param name="sessionCookies">Expires the session once the account is gone.</param>
    public MeController(
        ICurrentUserAccessor currentUser,
        IUserAccountService accounts,
        IDeviceService devices,
        IDataExportService dataExport,
        IAccountErasureService erasure,
        ISessionCookieWriter sessionCookies)
    {
        _currentUser = currentUser;
        _accounts = accounts;
        _devices = devices;
        _dataExport = dataExport;
        _erasure = erasure;
        _sessionCookies = sessionCookies;
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

        // A valid token for a user row that no longer exists. Ordinary after an
        // account erasure on another device: the cookie outlives the row, and 404
        // for "who am I?" is the honest answer.
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

    /// <summary>
    /// Streams everything held about the caller as a JSON download — the right of
    /// access (GDPR Art. 15) and data portability (Art. 20) in one endpoint.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the export as a file download.</returns>
    [HttpGet("export")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task ExportAsync(CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        // Written straight to the response body rather than returned as a result:
        // a complete position history can be very large, and buffering it into an
        // ActionResult would defeat the streaming the export service does.
        string fileName = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{ExportFileNameStem}-{userId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");

        Response.ContentType = "application/json";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        await _dataExport.WriteExportAsync(userId, Response.Body, cancellationToken);
    }

    /// <summary>
    /// Permanently erases the caller's account — the right to be forgotten
    /// (GDPR Art. 17). Irreversible, and it really deletes rows.
    /// </summary>
    /// <param name="request">The caller's current password, as proof of identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with a summary of what was removed, or 400 when the password is wrong.</returns>
    [HttpDelete]
    [ProducesResponseType(typeof(AccountErasureResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountErasureResultDto>> DeleteAccountAsync(
        [FromBody] DeleteAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        int userId = RequireUserId(_currentUser);

        OperationResult<AccountErasureSummary> result =
            await _erasure.EraseAsync(userId, request.Password, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        // The account is gone; the cookie must go with it, or the browser keeps
        // presenting a token for a user row that no longer exists.
        _sessionCookies.Clear(Response);

        AccountErasureSummary summary = result.Value!;

        return Ok(new AccountErasureResultDto(
            summary.DevicesDeleted,
            summary.DevicesRetained,
            summary.PositionsDeleted,
            summary.GrantsDeleted,
            summary.GrantsAnonymised));
    }
}
