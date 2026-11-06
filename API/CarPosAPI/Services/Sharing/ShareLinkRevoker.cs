using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Revokes the share links that lose their footing when access changes: the links
/// one person made on a device, once that person can no longer share it, and every
/// link on a device that is being deleted.
///
/// <para>
/// <b>Why this exists.</b> A share link is a credential somebody created on the
/// strength of their own <c>CanShare</c>, and it used to outlive that. A sharer who
/// was removed from a device kept every link they had made — up to
/// <c>Sharing:MaxWindowDays</c> of somebody else's whereabouts, in the hands of
/// strangers the owner never chose — and a deleted device kept showing its history
/// to anyone holding one. Revoking here closes both. <see cref="ShareViewService"/>
/// re-reads the row on every request, so the visitor's next refresh is already
/// refused.
/// </para>
///
/// <para>
/// <b>Every link not yet revoked, expired ones included.</b> Expiry is not terminal
/// here: <see cref="ShareLinkService"/> lets an expired link be edited back into a
/// live window. Revocation is the one state that cannot be undone, which is why it
/// is the one applied.
/// </para>
///
/// <para>
/// <b>Marked, not deleted</b>, exactly as a manual revoke does, so the device's link
/// list still shows what happened. The rows are tracked and changed in memory only:
/// the caller's own <c>SaveChangesAsync</c> commits them together with the grant or
/// device change that caused them, so the two can never be half-applied.
/// </para>
///
/// Static and stateless — it works on the context it is handed.
/// </summary>
internal static class ShareLinkRevoker
{
    /// <summary>
    /// Revokes every unrevoked link one person created on one device. Called when
    /// that person's grant is revoked or loses <c>CanShare</c>.
    /// </summary>
    /// <param name="context">The caller's context; the changes are left for it to save.</param>
    /// <param name="deviceRowId">Internal row id of the device.</param>
    /// <param name="createdByUserId">The account whose links go.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many links were marked revoked.</returns>
    public static Task<int> RevokeCreatedByAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        int createdByUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return RevokeAsync(
            context.ShareLinks.Where(link => link.DeviceId == deviceRowId && link.CreatedByUserId == createdByUserId),
            cancellationToken);
    }

    /// <summary>
    /// Revokes every unrevoked link on a device, whoever created it. Called when the
    /// device is deleted.
    /// </summary>
    /// <param name="context">The caller's context; the changes are left for it to save.</param>
    /// <param name="deviceRowId">Internal row id of the device.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many links were marked revoked.</returns>
    public static Task<int> RevokeAllAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return RevokeAsync(
            context.ShareLinks.Where(link => link.DeviceId == deviceRowId),
            cancellationToken);
    }

    /// <summary>Marks the unrevoked links among <paramref name="links"/> as revoked now.</summary>
    /// <param name="links">The candidate links, already narrowed to one device.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many links were marked revoked.</returns>
    private static async Task<int> RevokeAsync(IQueryable<ShareLink> links, CancellationToken cancellationToken)
    {
        // Tracked on purpose (no AsNoTracking): the caller's SaveChangesAsync is what
        // writes these, in the same unit of work as the change that triggered them.
        List<ShareLink> unrevoked = await links
            .Where(link => link.RevokedAt == null)
            .ToListAsync(cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        foreach (ShareLink link in unrevoked)
        {
            link.RevokedAt = nowUtc;
        }

        return unrevoked.Count;
    }
}
