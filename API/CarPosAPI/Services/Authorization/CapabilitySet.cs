using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Authorization;

/// <summary>
/// The three capabilities a client may ask for on a grant, after the server's
/// invariants have been applied. Read access is not among them because it is
/// implied by the grant existing at all.
///
/// This type exists so the coercion rule lives in exactly one place. Every entry
/// point that sets a grant's capabilities — creating an invitation
/// (<c>POST /api/access/invitations</c>, whose capabilities the accepted grant
/// receives) and editing a grant (<c>PUT /api/access</c>) — funnels through
/// <see cref="FromRequest"/>, so none can forget it.
///
/// It also answers the escalation question, <see cref="GainsBeyond"/>: a sharer may
/// hand out only what they hold. Coercion alone never made that true — a sharer
/// without <c>CanDelete</c> could tick it for somebody else, or for a second account
/// of their own. Provisioning cannot escalate (the creator holds the full set), so
/// only the sharing endpoints need the check; see
/// <see cref="Sharing.GrantChangePolicy"/>.
/// </summary>
/// <param name="CanDelete">May soft-delete the device.</param>
/// <param name="CanShare">May grant and revoke others' access.</param>
/// <param name="CanModifySettings">May change settings and read the firmware block.</param>
public sealed record CapabilitySet(bool CanDelete, bool CanShare, bool CanModifySettings)
{
    /// <summary>
    /// Applies the server-side invariant to a requested capability set: sharing
    /// implies settings.
    ///
    /// It <em>coerces</em> rather than rejects, deliberately. Someone ticking
    /// "can share" has said what they mean; answering with a validation error
    /// about an implication they never thought about would be pedantry, and the
    /// combination they asked for is incoherent anyway — a user who may hand out
    /// settings rights but not hold them can simply grant them to themselves.
    /// </summary>
    /// <param name="canDelete">Requested delete capability.</param>
    /// <param name="canShare">Requested share capability.</param>
    /// <param name="canModifySettings">Requested settings capability.</param>
    /// <returns>The coerced set that will actually be stored.</returns>
    public static CapabilitySet FromRequest(bool canDelete, bool canShare, bool canModifySettings)
    {
        return new CapabilitySet(canDelete, canShare, canModifySettings || canShare);
    }

    /// <summary>The full set granted to whoever registers a device.</summary>
    /// <returns>A set with every capability enabled.</returns>
    public static CapabilitySet Full()
    {
        return new CapabilitySet(true, true, true);
    }

    /// <summary>The empty set: what a user who has no grant yet holds.</summary>
    /// <returns>A set with every capability disabled.</returns>
    public static CapabilitySet None()
    {
        return new CapabilitySet(false, false, false);
    }

    /// <summary>The set an existing grant, or a caller's resolved permissions, carries.</summary>
    /// <param name="permissions">The grant's flags.</param>
    /// <returns>The three capabilities, read access left out as everywhere else.</returns>
    public static CapabilitySet Of(DevicePermissionsDto permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return new CapabilitySet(permissions.CanDelete, permissions.CanShare, permissions.CanModifySettings);
    }

    /// <summary>
    /// Whether storing this set would give the grantee a capability that neither
    /// they already had nor the granter holds — that is, whether it would hand out
    /// something the granter does not have to give.
    ///
    /// A capability the grantee already holds may be kept (a full-replacement
    /// <c>PUT</c> has to echo it back) or taken away; only a <em>gain</em> is
    /// measured against the granter.
    /// </summary>
    /// <param name="granter">What the caller holds on the device.</param>
    /// <param name="current">What the grantee holds now; <see cref="None"/> for a new grant.</param>
    /// <returns>True when at least one capability would be gained beyond the granter's own.</returns>
    public bool GainsBeyond(CapabilitySet granter, CapabilitySet current)
    {
        ArgumentNullException.ThrowIfNull(granter);
        ArgumentNullException.ThrowIfNull(current);

        return (CanDelete && !current.CanDelete && !granter.CanDelete)
            || (CanShare && !current.CanShare && !granter.CanShare)
            || (CanModifySettings && !current.CanModifySettings && !granter.CanModifySettings);
    }
}
