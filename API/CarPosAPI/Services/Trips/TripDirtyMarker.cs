using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Marks a device's trips as needing recomputation from a given fix time — the only way
/// anything asks the trip worker for work.
///
/// <para>
/// An upsert on <c>trip_detection_state</c>, so it also takes that row's lock: called
/// inside a writer's transaction, it serialises the writer with the worker's apply step
/// (which locks the same row first). <c>dirty_from</c> only ever moves earlier here —
/// <c>LEAST</c> ignores a NULL, so a clean device simply takes the new value.
/// </para>
///
/// <para>
/// <b>Invalidating.</b> A change that makes an in-flight computation wrong — erased
/// positions, new thresholds — also bumps <c>epoch</c>; the worker compares it under the
/// lock and throws away a result computed before the change. New positions do not need
/// to: they only ever add to what the worker will see on its next pass.
/// </para>
/// </summary>
internal static class TripDirtyMarker
{
    /// <summary>Lowers the device's dirty mark to <paramref name="dirtyFromUtc"/>.</summary>
    /// <param name="context">The context whose transaction (if any) the upsert joins.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="dirtyFromUtc">Earliest fix time whose trips may have changed (UTC).</param>
    /// <param name="invalidateInFlight">True to also bump the epoch (see the class summary).</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row is written.</returns>
    public static Task<int> MarkAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        DateTime dirtyFromUtc,
        bool invalidateInFlight,
        CancellationToken cancellationToken)
    {
        return invalidateInFlight
            ? context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO trip_detection_state (device_id, dirty_from, epoch)
                 VALUES ({deviceRowId}, {dirtyFromUtc}, 1)
                 ON CONFLICT (device_id) DO UPDATE
                 SET dirty_from = LEAST(trip_detection_state.dirty_from, EXCLUDED.dirty_from),
                     epoch = trip_detection_state.epoch + 1
                 """,
                cancellationToken)
            : context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO trip_detection_state (device_id, dirty_from)
                 VALUES ({deviceRowId}, {dirtyFromUtc})
                 ON CONFLICT (device_id) DO UPDATE
                 SET dirty_from = LEAST(trip_detection_state.dirty_from, EXCLUDED.dirty_from)
                 """,
                cancellationToken);
    }
}
