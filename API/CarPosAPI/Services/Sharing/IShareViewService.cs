using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Reads positions on behalf of an anonymous share visitor.
///
/// <para>
/// Deliberately <b>not</b> a method on <see cref="Positions.IPositionQueryService"/>
/// and deliberately takes no user id. The two services answer different questions
/// and trust different things, and keeping them apart is what makes it impossible
/// for a share id to reach a code path that expects an account — or for a widening
/// of one to silently widen the other.
/// </para>
/// </summary>
public interface IShareViewService
{
    /// <summary>
    /// Resolves a share and returns the fixes it currently exposes.
    ///
    /// Re-reads the link on every call, so revocation and expiry take effect
    /// immediately rather than whenever the visitor's token happens to lapse.
    /// </summary>
    /// <param name="shareId">The share from the caller's token claim.</param>
    /// <param name="fromUtc">Requested lower bound, clamped to the window. Ignored for a latest-only share.</param>
    /// <param name="toUtc">Requested upper bound, clamped to the window. Ignored for a latest-only share.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The share and its fixes, or the reason access has ended.</returns>
    Task<OperationResult<SharedViewDto>> GetAsync(
        Guid shareId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken);
}
