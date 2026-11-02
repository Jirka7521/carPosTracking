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
/// Two rules, both about escalation. <c>CanShare</c> used to be a key to
/// everything: a sharer could edit their own grant (ticking <c>CanDelete</c> for
/// themselves) and could hand anyone, a second account of their own included,
/// capabilities they never held. So:
/// </para>
/// <list type="bullet">
/// <item>Nobody changes the capabilities on their own grant. Somebody else who can
/// share the device has to do it. Revoking one's own grant stays possible — it only
/// ever takes rights away, and the last-sharer rule still applies.</item>
/// <item>A grant may only <em>gain</em> capabilities the caller holds. Keeping or
/// removing one the grantee already has is not a gain.</item>
/// </list>
///
/// <para>
/// The third rule — the last sharer cannot be removed or demoted — needs a count
/// over the device's other grants, so it stays in the service.
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

    /// <summary>Checks a new grant (or the reactivation of a revoked one).</summary>
    /// <param name="granter">What the caller holds on the device.</param>
    /// <param name="requested">The coerced capability set to be stored.</param>
    /// <returns>Null when allowed; otherwise the refusal, to be answered as 403.</returns>
    public static ServiceError? CheckCreate(CapabilitySet granter, CapabilitySet requested)
    {
        ArgumentNullException.ThrowIfNull(granter);
        ArgumentNullException.ThrowIfNull(requested);

        // A reactivated grant starts from nothing as well: the revoked row's old
        // flags are history, not something the grantee still holds.
        return requested.GainsBeyond(granter, CapabilitySet.None()) ? UnheldCapability : null;
    }

    /// <summary>Checks a change to the capability set on an existing grant.</summary>
    /// <param name="callerId">The authenticated caller.</param>
    /// <param name="granter">What the caller holds on the device.</param>
    /// <param name="granteeId">The user the grant belongs to.</param>
    /// <param name="current">What the grant carries now.</param>
    /// <param name="requested">The coerced capability set to be stored.</param>
    /// <returns>Null when allowed; otherwise the refusal, to be answered as 403.</returns>
    public static ServiceError? CheckUpdate(
        int callerId,
        CapabilitySet granter,
        int granteeId,
        CapabilitySet current,
        CapabilitySet requested)
    {
        ArgumentNullException.ThrowIfNull(granter);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(requested);

        // Checked first and unconditionally — even a no-op or a pure demotion. The
        // rule is easier to reason about as "never your own" than as "never your own
        // upwards", and the dashboard never offers it (it hides the caller's row).
        if (callerId == granteeId)
        {
            return OwnGrant;
        }

        return requested.GainsBeyond(granter, current) ? UnheldCapability : null;
    }
}
