using System.ComponentModel.DataAnnotations;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Sharing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CarPosAPI.Controllers;

/// <summary>
/// Sharing a device by invitation link — the only way another account gains access.
///
/// <para>
/// A sharer makes a single-use link; the dashboard shows it once and the sharer sends
/// it however they like. Whoever holds it signs in, previews the offer, and accepts or
/// declines. Every action needs a session, and the three that take a link carry the
/// token in the request <em>body</em>, never the URL, so it stays out of access logs.
/// </para>
///
/// <para>
/// Each action names its rate-limit policy itself (per account), so the
/// <c>EndpointAccessMetadataTests</c> can hold it to account.
/// </para>
/// </summary>
[Route("api/access/invitations")]
[Authorize]
public sealed class AccessInvitationsController : ApiControllerBase
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAccessInvitationService _invitations;

    /// <summary>Creates the controller.</summary>
    /// <param name="currentUser">Supplies the caller's id.</param>
    /// <param name="invitations">Does the invitation work and authorises each call.</param>
    public AccessInvitationsController(ICurrentUserAccessor currentUser, IAccessInvitationService invitations)
    {
        _currentUser = currentUser;
        _invitations = invitations;
    }

    /// <summary>Makes an invitation link for a device.</summary>
    /// <param name="request">The device and the capabilities on offer.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the invitation and its one-time token, or the reason it was refused.</returns>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Invitations)]
    [ProducesResponseType(typeof(AccessInvitationCreatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccessInvitationCreatedDto>> CreateAsync(
        [FromBody] AccessInvitationCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        OperationResult<AccessInvitationCreatedDto> result =
            await _invitations.CreateAsync(RequireUserId(_currentUser), request, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Lists a device's open invitations (never their tokens).</summary>
    /// <param name="deviceId">The device's MQTT identity. Required.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the invitations, 403 without <c>CanShare</c>, 404 when not visible.</returns>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Invitations)]
    [ProducesResponseType(typeof(IReadOnlyList<AccessInvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AccessInvitationDto>>> ListAsync(
        [FromQuery][Required][StringLength(64, MinimumLength = 1)] string deviceId,
        CancellationToken cancellationToken)
    {
        OperationResult<IReadOnlyList<AccessInvitationDto>> result =
            await _invitations.ListForDeviceAsync(RequireUserId(_currentUser), deviceId, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Withdraws an invitation; its link stops working at once.</summary>
    /// <param name="invitationId">The invitation to withdraw.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204, or 404 for an invitation the caller cannot see.</returns>
    [HttpDelete("{invitationId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.Invitations)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WithdrawAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        OperationResult<bool> result =
            await _invitations.WithdrawAsync(RequireUserId(_currentUser), invitationId, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }

    /// <summary>Shows what an invitation link offers, before deciding.</summary>
    /// <param name="request">The link's token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the offer, or why the link cannot be used.</returns>
    [HttpPost("preview")]
    [EnableRateLimiting(RateLimitPolicies.Invitations)]
    [ProducesResponseType(typeof(AccessInvitationPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccessInvitationPreviewDto>> PreviewAsync(
        [FromBody] AccessInvitationTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<AccessInvitationPreviewDto> result =
            await _invitations.PreviewAsync(RequireUserId(_currentUser), request.Token, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Accepts an invitation: the caller gets the offered access.</summary>
    /// <param name="request">The link's token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 with the new grant, 409 when the caller already has access, or why the link cannot be used.</returns>
    [HttpPost("accept")]
    [EnableRateLimiting(RateLimitPolicies.Invitations)]
    [ProducesResponseType(typeof(AccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccessDto>> AcceptAsync(
        [FromBody] AccessInvitationTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<AccessDto> result =
            await _invitations.AcceptAsync(RequireUserId(_currentUser), request.Token, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : Failure(result);
    }

    /// <summary>Declines an invitation: nothing is shared and the link is spent.</summary>
    /// <param name="request">The link's token.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204, or why the link cannot be used.</returns>
    [HttpPost("decline")]
    [EnableRateLimiting(RateLimitPolicies.Invitations)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineAsync(
        [FromBody] AccessInvitationTokenRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<bool> result =
            await _invitations.DeclineAsync(RequireUserId(_currentUser), request.Token, cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result);
    }
}
