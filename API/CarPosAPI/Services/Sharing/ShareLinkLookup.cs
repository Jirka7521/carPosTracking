using CarPosAPI.Data.Entities;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// The result of looking up a share link <em>and</em> checking that the caller may
/// administer it — the two questions every mutating share endpoint has to answer
/// before it does anything.
///
/// The share-link counterpart of <see cref="GrantLookup"/>, and it exists for the
/// same reason: <see cref="ShareLinkService.UpdateAsync"/> and
/// <see cref="ShareLinkService.RevokeAsync"/> return differently-typed results but
/// must reach them through one authorisation path, so that path cannot drift
/// between the two. Internal: purely an implementation detail of that service.
/// </summary>
/// <param name="Link">The link being addressed, when the lookup succeeded. Tracked, so callers may mutate it.</param>
/// <param name="Caller">The caller's own context on the link's device, when it succeeded.</param>
/// <param name="Failure">The failure to report to the caller, when it did not.</param>
/// <param name="FailureOutcome">Which failure it was; meaningless unless <paramref name="Failure"/> is set.</param>
internal sealed record ShareLinkLookup(
    ShareLink? Link,
    DeviceAccessContext? Caller,
    ServiceError? Failure,
    OperationOutcome FailureOutcome)
{
    /// <summary>Builds a successful lookup.</summary>
    /// <param name="link">The link that was found.</param>
    /// <param name="caller">The caller's context on its device.</param>
    /// <returns>A lookup with no failure set.</returns>
    public static ShareLinkLookup Found(ShareLink link, DeviceAccessContext caller)
    {
        return new ShareLinkLookup(link, caller, null, OperationOutcome.Success);
    }

    /// <summary>Builds a failed lookup.</summary>
    /// <param name="outcome">Which kind of failure.</param>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="detail">Message for the caller.</param>
    /// <returns>A lookup carrying the failure.</returns>
    public static ShareLinkLookup Failed(OperationOutcome outcome, string code, string detail)
    {
        return new ShareLinkLookup(null, null, new ServiceError(code, detail), outcome);
    }
}
