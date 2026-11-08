using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// The one query every trip read is built on, kept in one place so the rule for which
/// journey a trip is in has exactly one definition.
///
/// <para>
/// <b>The journey rule, in SQL.</b> A pinned trip is in the journey stored on it. An
/// unpinned one is in the innermost journey whose <c>[range_from, range_to)</c> holds its
/// start — computed here, at query time, and never written back. Ranges on a device are
/// disjoint or nested (enforced by <c>JourneyService</c>), so "innermost" is simply the
/// containing range that starts latest; ties on the start are broken by the earlier end.
/// That is why editing or deleting a range needs no background job: the next read already
/// reflects it, for every trip, including those that arrive later.
/// </para>
/// </summary>
internal static class TripQueries
{
    /// <summary>All of a device's trips, each with its successor's start and its effective journey.</summary>
    /// <param name="context">The context to query.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <returns>A composable, untracked query.</returns>
    public static IQueryable<TripRow> Rows(CarPosDbContext context, Guid deviceRowId) =>
        context.Trips
            .AsNoTracking()
            .Where(trip => trip.DeviceId == deviceRowId)
            .Select(trip => new TripRow
            {
                Id = trip.Id,
                StartTime = trip.StartTime,
                NextStart = context.Trips
                    .Where(other => other.DeviceId == trip.DeviceId && other.StartTime > trip.StartTime)
                    .Min(other => (DateTime?)other.StartTime),
                EffectiveJourneyId = trip.JourneyPinned
                    ? trip.JourneyId
                    : context.Journeys
                        .Where(journey => journey.DeviceId == trip.DeviceId
                            && journey.RangeFrom <= trip.StartTime
                            && trip.StartTime < journey.RangeTo)
                        .OrderByDescending(journey => journey.RangeFrom)
                        .ThenBy(journey => journey.RangeTo)
                        .Select(journey => (Guid?)journey.Id)
                        .FirstOrDefault(),
                JourneyPinned = trip.JourneyPinned,
                Name = trip.Name,
            });

    /// <summary>
    /// One page of a filtered trip list, newest first, keyset-paged on the start.
    /// </summary>
    /// <param name="rows">The base query (<see cref="Rows"/>), already filtered.</param>
    /// <param name="before">Exclusive upper bound on the start (the previous page's cursor), or null.</param>
    /// <param name="take">Rows to fetch — one more than the page, to learn whether another follows.</param>
    /// <returns>The page query.</returns>
    public static IQueryable<TripRow> Page(IQueryable<TripRow> rows, DateTime? before, int take)
    {
        if (before is DateTime cursor)
        {
            rows = rows.Where(row => row.StartTime < cursor);
        }

        return rows
            .OrderByDescending(row => row.StartTime)
            .ThenByDescending(row => row.Id)
            .Take(take);
    }
}
