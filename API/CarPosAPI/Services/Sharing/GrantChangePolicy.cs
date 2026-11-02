using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// The rules on <em>what</em> a sharer may do to a grant, once
/// <see cref="AccessService"/> has established that they may manage the device's
/// sharing at all. Pure logic over capability sets — no database — so every rule
/// is pinned by a unit test rather than by hoping the query around it is right.
///
/// <para>
/// All of them are about escalation. <c>CanShare</c> used to be a key to
/// everything: a sharer could edit their own grant (ticking <c>CanDelete</c> for
/// themselves), hand anyone capabilities they never held, and demote or remove the
/// person who registered the device. So:
/// </para>
/// <list type="bullet">
/// <item>Nobody changes the capabilities on their own grant. Somebody else who can
/// share the device has to do it.</item>
/// <item>The owner's grant (<see cref="Data.Entities.Access.IsOwner"/>) is not
/// edited or revoked through sharing at all — by anyone, the owner included, since
/// ownership cannot be handed over and a device must keep its owner.</item>
/// <item>Only the owner gives or takes <c>CanDelete</c>: it is the one capability
/// that ends the device for everybody.</item>
/// <item>A grant may only <em>gain</em> capabilities the caller holds. Keeping or
/// removing one the grantee already has is not a gain.</item>
/// </list>
///
/// <para>
/// Revoking a non-owner grant — one's own included — stays possible: it only ever
/// takes rights away. The last-sharer rule needs a count over the device's other
/// grants, so it stays in the service.
/// </para>
/// </summary>
internal static class GrantChangePolicy
{
    /// <summary>The refusal for a sharer editing their own grant.</summary>
    private static readonly ServiceError OwnGrant = new ServiceError(
        ErrorCodes.CannotChangeOwnAccess,
        "You cannot change your own access to a device. Ask another person who can share it.");

    /// <summary>The refusal for handing out a capability the caller lacks.</summary>
    private static readonly ServiceError UnheldCapability = new ServiceError(
        ErrorCodes.CannotGrantUnheldCapability,
        "You can only give others permissions you hold yourself.");

    /// <summary>The refusal for touching the owner's grant.</summary>
    private static readonly ServiceError OwnerGrant = new ServiceError(
        ErrorCodes.OwnerAccessImmutable,
        "The access of the person who registered this device cannot be changed or removed.");

    /// <summary>The refusal for a non-owner giving or taking the delete right.</summary>
    private static readonly ServiceError DeleteRight = new ServiceError(
        ErrorCodes.OnlyOwnerManagesDelete,
        "Only the person who registered this device can give or take the right to delete it.");

    /// <summary>Checks a new grant (or the reactivation of a revoked one).</summary>
    /// <param name="caller">The caller and what they hold on the device.</param>
    /// <param name="requested">The coerced capability set to be stored.</param>
    /// <returns>Null when allowed; otherwise the refusal, to be answered as 403.</returns>
    public static ServiceError? CheckCreate(GrantParty caller, CapabilitySet requested)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(requested);

        if (requested.CanDelete && !caller.IsOwner)
        {
            return DeleteRight;
        }

        // A reactivated grant starts from nothing as well: the revoked row's old
        // flags are history, not something the grantee still holds.
        return requested.GainsBeyond(caller.Capabilities, CapabilitySet.None()) ? UnheldCapability : null;
    }

    /// <summary>Checks a change to the capability set on an existing grant.</summary>
    /// <param name="caller">The caller and what they hold on the device.</param>
    /// <param name="grantee">The grant's holder and what it carries now.</param>
    /// <param name="requested">The coerced capability set to be stored.</param>
    /// <returns>Null when allowed; otherwise the refusal, to be answered as 403.</returns>
    public static ServiceError? CheckUpdate(GrantParty caller, GrantParty grantee, CapabilitySet requested)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(grantee);
        ArgumentNullException.ThrowIfNull(requested);

        // Checked first and unconditionally — even a no-op or a pure demotion. The
        // rule is easier to reason about as "never your own" than as "never your own
        // upwards", and the dashboard never offers it (it hides the caller's row).
        if (caller.UserId == grantee.UserId)
        {
            return OwnGrant;
        }

        if (grantee.IsOwner)
        {
            return OwnerGrant;
        }

        if (requested.CanDelete != grantee.Capabilities.CanDelete && !caller.IsOwner)
        {
            return DeleteRight;
        }

        return requested.GainsBeyond(caller.Capabilities, grantee.Capabilities) ? UnheldCapability : null;
    }

    /// <summary>Checks revoking a grant.</summary>
    /// <param name="grantee">The grant's holder.</param>
    /// <returns>Null when allowed; otherwise the refusal, to be answered as 403.</returns>
    public static ServiceError? CheckRevoke(GrantParty grantee)
    {
        ArgumentNullException.ThrowIfNull(grantee);

        return grantee.IsOwner ? OwnerGrant : null;
    }
}
