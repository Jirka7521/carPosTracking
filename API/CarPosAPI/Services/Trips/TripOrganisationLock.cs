using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Serialises the writes that organise one device's trips — journeys
/// (<see cref="JourneyService"/>) and location rules (<see cref="LocationRuleService"/>) —
/// for the length of the caller's transaction.
///
/// <para>
/// A transaction-scoped advisory lock keyed on the device: the per-device caps, the
/// range-overlap check and "delete a journey, retarget its rules" each read a picture of
/// the device's journeys and rules and then write, and two writers must not both act on
/// a picture that excludes the other's change. The trip worker never takes it; it only
/// writes trip starts and ends, which neither check reads.
/// </para>
/// </summary>
internal static class TripOrganisationLock
{
    /// <summary>Takes the device's lock; released when the current transaction ends.</summary>
    /// <param name="context">The context whose open transaction holds the lock.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes once the lock is held.</returns>
    public static Task<int> AcquireAsync(CarPosDbContext context, Guid deviceRowId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({deviceRowId.ToString()}, 0))",
            cancellationToken);
}
