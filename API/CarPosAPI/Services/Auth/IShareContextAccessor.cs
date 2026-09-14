namespace CarPosAPI.Services.Auth;

/// <summary>
/// Tells a service which share the current request is acting on behalf of — the
/// anonymous counterpart of <see cref="ICurrentUserAccessor"/>.
///
/// The two are separate interfaces rather than one nullable-everything accessor
/// precisely so that a service written for accounts cannot be handed a share by
/// accident: there is no shared shape for the two to be confused through.
/// </summary>
public interface IShareContextAccessor
{
    /// <summary>
    /// The share link's id, or null when the request carries no valid share token.
    /// Behind the share scheme's <c>[Authorize]</c> it is never null.
    /// </summary>
    Guid? ShareId { get; }
}
