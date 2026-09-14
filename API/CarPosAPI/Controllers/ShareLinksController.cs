using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarPosAPI.Controllers;

/// <summary>
/// Temporary share links — the creator's side.
///
/// Every action requires <c>CanShare</c> on the device concerned, resolved
/// server-side from the caller's own grant. As in
/// <see cref="AccessController"/>, the id in the route identifies a row and never
/// authorises touching it.
/// </summary>
[Route("api/shares")]
[Authorize]
public sealed class ShareLinksController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IShareLinkService _shares;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="shares">Does the work and authorises each call.</param>
    public ShareLinksController(ICurrentUserAccessor currentUser, IShareLinkService shares)
    {
        _currentUser = currentUser;
        _shares = shares;
    }

    /// <summary>Lists every share link on a device, live and dead.</summary>
    /// <param name="deviceId">The device's MQTT identity. Required.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the links, 403 without <c>CanShare</c>, 404 when not visible.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ShareLinkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ShareLinkDto>>> ListAsync(
        [FromQuery][Required][StringLength(64, MinimumLength = 1)] string deviceId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<IReadOnlyList<ShareLinkDto>> result =
            await _shares.ListForDeviceAsync(userId, deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Mints a share link.
    ///
    /// <b>The 201 body is the only time the link and its code exist outside the
    /// creator's screen.</b> Neither is stored in recoverable form, so a client that
    /// discards this response has discarded the share.
    /// </summary>
    /// <param name="request">Device, window, scope and telemetry choices.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>201 with the link and its secrets, or the reason it was refused.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ShareLinkCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShareLinkCreatedDto>> CreateAsync(
        [FromBody] ShareLinkCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<ShareLinkCreatedDto> result = await _shares.CreateAsync(userId, request, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result);
        }

        // Deliberately no Location header. The convention elsewhere in this API is to
        // point at the created resource, but there is no endpoint that returns one
        // share link by id — and there is not going to be, because the only thing
        // such a URL could usefully add is the secret it must never reveal.
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Changes an existing link's window, label, scope and telemetry flags.
    ///
    /// <b>The link and its code are untouched</b>, so whoever already holds them
    /// keeps working access — which is the point of editing rather than reissuing,
    /// and also why widening the window here is a disclosure decision rather than a
    /// settings tweak. A revoked link answers 409: revocation is deliberate and
    /// stays that way.
    /// </summary>
    /// <param name="shareId">The link to change.</param>
    /// <param name="request">The new settings — a full replacement, not a patch.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the updated link, or the reason it was refused.</returns>
    [HttpPut("{shareId:guid}")]
    [ProducesResponseType(typeof(ShareLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShareLinkDto>> UpdateAsync(
        Guid shareId,
        [FromBody] ShareLinkUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<ShareLinkDto> result =
            await _shares.UpdateAsync(userId, shareId, request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Mints a fresh link and code for an existing share, keeping its settings.
    ///
    /// <b>The only answer to "show me the link again"</b>, because the originals
    /// are stored as hashes and do not exist anywhere to be shown. The previous
    /// link and code stop working immediately; the window, scope, label and redeem
    /// history carry over.
    /// </summary>
    /// <param name="shareId">The link to reissue.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the share and its new one-time secrets, or the reason it was refused.</returns>
    [HttpPost("{shareId:guid}/reissue")]
    [ProducesResponseType(typeof(ShareLinkCreatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShareLinkCreatedDto>> ReissueAsync(
        Guid shareId,
        CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<ShareLinkCreatedDto> result =
            await _shares.ReissueAsync(userId, shareId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>
    /// Revokes a share link.
    ///
    /// Takes effect on the visitor's very next request: the view path re-reads the
    /// row rather than trusting the token it was given.
    /// </summary>
    /// <param name="shareId">The link to withdraw.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 on success, or the reason it was refused.</returns>
    [HttpDelete("{shareId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(Guid shareId, CancellationToken cancellationToken)
    {
        int userId = RequireUserId(_currentUser);

        OperationResult<bool> result = await _shares.RevokeAsync(userId, shareId, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }
}
