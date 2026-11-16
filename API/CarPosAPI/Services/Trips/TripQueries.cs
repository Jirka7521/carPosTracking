using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// The one query every trip read is built on, kept in one place so the rule for which
/// journey a trip is in has exactly one definition.
///
/// <para>
/// <b>The journey rule lives in the database.</b> It reads the
/// <c>trip_effective_journeys</c> view (see <see cref="Data.Entities.TripEffectiveJourney"/>):
/// a pinned trip is in the journey stored on it; otherwise the best location rule in force
/// at its start whose circle holds its start or end wins; otherwise the innermost journey
/// whose <c>[range_from, range_to)</c> holds its start. Location matching is PostGIS
/// <c>ST_DWithin</c> on the trip's fixes, which EF cannot express without a spatial
/// dependency — so the whole rule is SQL, and this method only filters and projects it.
/// Nothing is ever written back: editing a rule or a range needs no background job, and
/// the next read reflects it for every trip, including those that arrive later.
/// </para>
/// </summary>
internal static class TripQueries
{
    /// <summary>All of a device's trips, each with its successor's start and its effective journey.</summary>
    /// <param name="context">The context to query.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <returns>A composable, untracked query.</returns>
    public static IQueryable<TripRow> Rows(CarPosDbContext context, Guid deviceRowId) =>
        context.TripEffectiveJourneys
            .AsNoTracking()
            .Where(row => row.DeviceId == deviceRowId)
            .Select(row => new TripRow
            {
                Id = row.TripId,
                StartTime = row.StartTime,
                NextStart = row.NextStart,
                EffectiveJourneyId = row.EffectiveJourneyId,
                JourneySource = row.JourneySource,
                LocationRuleId = row.LocationRuleId,
                LocationRuleAtEnd = row.LocationRuleAtEnd,
                JourneyPinned = row.JourneyPinned,
                Name = row.Name,
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
