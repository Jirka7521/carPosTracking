using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Sharing a device with another account by invitation link — the only way a grant
/// comes into being after a device is registered.
///
/// A sharer makes a single-use link carrying a set of capabilities; whoever holds it
/// signs in, looks at what is offered, and accepts or declines. Nothing is shared,
/// and neither side is shown to the other, until the recipient accepts. Every check
/// that held when the link was made is made again at acceptance, against the sharer's
/// access as it is <em>then</em>.
/// </summary>
public interface IAccessInvitationService
{
    /// <summary>Makes an invitation link for a device.</summary>
    /// <param name="userId">The authenticated caller; needs <c>CanShare</c> on an active device.</param>
    /// <param name="request">The device and the capabilities the accepted grant will carry.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The invitation and its one-time token, or the reason it was refused.</returns>
    Task<OperationResult<AccessInvitationCreatedDto>> CreateAsync(
        int userId,
        AccessInvitationCreateRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Lists a device's open (unexpired) invitations, without their tokens.</summary>
    /// <param name="userId">The authenticated caller; needs <c>CanShare</c>.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The invitations, or the reason the caller may not see them.</returns>
    Task<OperationResult<IReadOnlyList<AccessInvitationDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);

    /// <summary>Withdraws an invitation so its link stops working at once.</summary>
    /// <param name="userId">The authenticated caller; needs <c>CanShare</c> on the invitation's device.</param>
    /// <param name="invitationId">The invitation to withdraw.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success, or 404 for an invitation the caller cannot see.</returns>
    Task<OperationResult<bool>> WithdrawAsync(int userId, Guid invitationId, CancellationToken cancellationToken);

    /// <summary>Describes what an invitation link offers, for the holder to decide on.</summary>
    /// <param name="userId">The authenticated holder of the link.</param>
    /// <param name="token">The link's token.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The offer, or why the link cannot be used.</returns>
    Task<OperationResult<AccessInvitationPreviewDto>> PreviewAsync(
        int userId,
        string token,
        CancellationToken cancellationToken);

    /// <summary>Accepts an invitation: the caller gets the offered grant and the link is spent.</summary>
    /// <param name="userId">The authenticated holder of the link.</param>
    /// <param name="token">The link's token.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The new grant, or why the link cannot be used.</returns>
    Task<OperationResult<AccessDto>> AcceptAsync(
        int userId,
        string token,
        CancellationToken cancellationToken);

    /// <summary>Declines an invitation: the link is spent and nothing is shared.</summary>
    /// <param name="userId">The authenticated holder of the link.</param>
    /// <param name="token">The link's token.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success, or why the link cannot be used.</returns>
    Task<OperationResult<bool>> DeclineAsync(
        int userId,
        string token,
        CancellationToken cancellationToken);
}
