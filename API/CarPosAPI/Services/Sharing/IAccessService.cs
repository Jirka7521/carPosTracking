using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Managing who else may see a device. Every method requires the caller to hold
/// <c>CanShare</c> on the device in question — the ability to hand out access is
/// itself a permission, not something ownership implies.
/// </summary>
public interface IAccessService
{
    /// <summary>Lists the active grants on a device.</summary>
    /// <param name="userId">The authenticated caller; needs <c>CanShare</c>.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The grants, or the reason the caller may not see them. Seeing the full list
    /// of who has access is restricted to sharers: for a read-only viewer it is
    /// simply a list of colleagues' accounts.
    /// </returns>
    Task<OperationResult<IReadOnlyList<AccessDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);

    /// <summary>Grants a user access to a device.</summary>
    /// <param name="userId">The authenticated caller; needs <c>CanShare</c>.</param>
    /// <param name="request">Who to share with, and with which capabilities.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The new grant, or a conflict when one is already active.</returns>
    Task<OperationResult<AccessDto>> CreateAsync(
        int userId,
        AccessCreateRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Replaces the capability set on an existing grant.</summary>
    /// <param name="userId">The authenticated caller; needs <c>CanShare</c> on the grant's device.</param>
    /// <param name="accessId">The grant being changed.</param>
    /// <param name="request">The new capability set.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The updated grant, or the reason the caller may not change it.</returns>
    Task<OperationResult<AccessDto>> UpdateAsync(
        int userId,
        int accessId,
        AccessUpdateRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Revokes a grant (soft — the row is deactivated, not deleted).</summary>
    /// <param name="userId">The authenticated caller; needs <c>CanShare</c> on the grant's device.</param>
    /// <param name="accessId">The grant being revoked.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success, or the reason the caller may not revoke it.</returns>
    Task<OperationResult<bool>> RevokeAsync(int userId, int accessId, CancellationToken cancellationToken);
}
