using CarPosAPI.Data.Entities;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// The result of looking up a grant <em>and</em> checking that the caller may
/// administer it — the two questions every mutating sharing endpoint has to
/// answer before it does anything.
///
/// It exists so <see cref="AccessService.UpdateAsync"/> and
/// <see cref="AccessService.RevokeAsync"/> can share one authorisation path
/// despite returning differently-typed results. Internal: purely an
/// implementation detail of that service.
/// </summary>
/// <param name="Grant">The grant being addressed, when the lookup succeeded.</param>
/// <param name="Caller">The caller's own context on the same device, when it succeeded.</param>
/// <param name="Failure">A message for the caller, when it did not.</param>
/// <param name="FailureOutcome">Which failure it was; meaningless unless <paramref name="Failure"/> is set.</param>
internal sealed record GrantLookup(
    Access? Grant,
    DeviceAccessContext? Caller,
    string? Failure,
    OperationOutcome FailureOutcome)
{
    /// <summary>Builds a successful lookup.</summary>
    /// <param name="grant">The grant that was found.</param>
    /// <param name="caller">The caller's context on its device.</param>
    /// <returns>A lookup with no failure set.</returns>
    public static GrantLookup Found(Access grant, DeviceAccessContext caller)
    {
        return new GrantLookup(grant, caller, null, OperationOutcome.Success);
    }

    /// <summary>Builds a failed lookup.</summary>
    /// <param name="outcome">Which kind of failure.</param>
    /// <param name="detail">Message for the caller.</param>
    /// <returns>A lookup carrying the failure.</returns>
    public static GrantLookup Failed(OperationOutcome outcome, string detail)
    {
        return new GrantLookup(null, null, detail, outcome);
    }
}
