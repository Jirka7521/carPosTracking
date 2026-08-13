namespace CarPosAPI.Services.Authorization;

/// <summary>
/// The three capabilities a client may ask for on a grant, after the server's
/// invariants have been applied. Read access is not among them because it is
/// implied by the grant existing at all.
///
/// This type exists so the coercion rule lives in exactly one place. Both entry
/// points that create grants — provisioning a device with
/// <c>additionalAccesses</c>, and <c>POST</c>/<c>PUT /api/access</c> — funnel
/// through <see cref="FromRequest"/>, so neither can forget it.
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
}
