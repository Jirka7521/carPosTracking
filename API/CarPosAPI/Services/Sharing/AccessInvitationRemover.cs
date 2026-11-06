using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Removes the open invitations that lose their footing when access changes: those
/// one person made on a device, once that person can no longer share it, and every
/// one on a device that is being deleted. The companion of <see cref="ShareLinkRevoker"/>,
/// called from the same three places.
///
/// <para>
/// Acceptance already re-checks the maker's access (<see cref="AccessInvitationService"/>),
/// so such an invitation could not be accepted anyway. Removing it is about the
/// sharing list telling the truth — a pending invitation that can never be accepted
/// is a row that looks like a live credential — and about not keeping a link around
/// any longer than the right that issued it.
/// </para>
///
/// <para>
/// Deleted rather than marked: an invitation has no history worth keeping, and
/// accepting, declining and withdrawing all delete it too. Tracked and left for the
/// caller's own <c>SaveChangesAsync</c>, so it commits with the change that caused it.
/// </para>
///
/// Static and stateless — it works on the context it is handed.
/// </summary>
internal static class AccessInvitationRemover
{
    /// <summary>Removes every open invitation one person made on one device.</summary>
    /// <param name="context">The caller's context; the removals are left for it to save.</param>
    /// <param name="deviceRowId">Internal row id of the device.</param>
    /// <param name="createdByUserId">The account whose invitations go.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many invitations were removed.</returns>
    public static Task<int> RemoveCreatedByAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        int createdByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return RemoveAsync(
            context,
            context.AccessInvitations.Where(invitation =>
                invitation.DeviceId == deviceRowId && invitation.CreatedByUserId == createdByUserId),
            cancellationToken);
    }

    /// <summary>Removes every open invitation on a device, whoever made it.</summary>
    /// <param name="context">The caller's context; the removals are left for it to save.</param>
    /// <param name="deviceRowId">Internal row id of the device.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many invitations were removed.</returns>
    public static Task<int> RemoveAllAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return RemoveAsync(
            context,
            context.AccessInvitations.Where(invitation => invitation.DeviceId == deviceRowId),
            cancellationToken);
    }

    /// <summary>Stages the removal of the given invitations.</summary>
    /// <param name="context">The caller's context.</param>
    /// <param name="invitations">The invitations, already narrowed to one device.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many invitations were staged for removal.</returns>
    private static async Task<int> RemoveAsync(
        CarPosDbContext context,
        IQueryable<AccessInvitation> invitations,
        CancellationToken cancellationToken)
    {
        List<AccessInvitation> doomed = await invitations.ToListAsync(cancellationToken);

        context.AccessInvitations.RemoveRange(doomed);

        return doomed.Count;
    }
}
