using CarPosAPI.Data;
using CarPosAPI.Services.Trips;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Tests;

/// <summary>
/// Proves the trip worker's reads of <c>positions</c> run in the database, bounded.
///
/// <para>
/// <c>positions</c> grows without bound, and the trip worker reads it on every save. A
/// grouping or a cap that EF quietly evaluated client-side would drag the history home
/// once per fix. As in <see cref="DeviceEventQueryTranslationTests"/>,
/// <c>ToQueryString()</c> renders the real Npgsql SQL without opening a connection.
/// </para>
/// </summary>
public sealed class TripQueryTranslationTests
{
    private const string UnusedConnectionString =
        "Host=localhost;Database=carpos_translation_check;Username=none;Password=none";

    private static CarPosDbContext CreateContext()
    {
        DbContextOptions<CarPosDbContext> options = new DbContextOptionsBuilder<CarPosDbContext>()
            .UseNpgsql(UnusedConnectionString)
            .Options;

        return new CarPosDbContext(options);
    }

    [Fact]
    public void NewPositionsAreCappedAndSummarisedPerDeviceInSql()
    {
        using CarPosDbContext context = CreateContext();

        string sql = TripDetectionPass.BuildArrivalQuery(context, 12_345, TripDetectionPass.ArrivalScanLimit)
            .ToQueryString();

        Assert.Contains("positions", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".id >", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheEffectiveJourneyIsComputedInSql_AndTheListFiltersAndPagesOnIt()
    {
        using CarPosDbContext context = CreateContext();
        Guid journeyId = Guid.NewGuid();

        IQueryable<TripRow> rows = TripQueries.Rows(context, Guid.NewGuid())
            .Where(row => row.EffectiveJourneyId == journeyId);
        string sql = TripQueries.Page(rows, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), 51).ToQueryString();

        // Pinned → stored journey, else a location rule, else the innermost containing
        // range: all decided by the database view (PostGIS for the places), which the
        // query filters on — never a client-side lookup.
        Assert.Contains("trip_effective_journeys", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("effective_journey_id =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("device_id =", sql, StringComparison.OrdinalIgnoreCase);

        // The successor's start (for bounding each trip's rebuild) and how the trip got
        // its journey come straight from the view.
        Assert.Contains("next_start", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("journey_source", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("location_rule_id", sql, StringComparison.OrdinalIgnoreCase);

        // Keyset paging on the start, newest first, capped.
        Assert.Contains("start_time <", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("start_time DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JourneyTripCounts_AreGroupedInTheDatabase()
    {
        // The sidebar's counts: one row per effective journey, not one per trip.
        using CarPosDbContext context = CreateContext();

        string sql = TripQueries.Rows(context, Guid.NewGuid())
            .GroupBy(row => row.EffectiveJourneyId)
            .Select(group => new JourneyTripCount(group.Key, group.Count()))
            .ToQueryString();

        Assert.Contains("trip_effective_journeys", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocationRulePreview_IsCountedAndSearchedInTheDatabase()
    {
        using CarPosDbContext context = CreateContext();
        LocationRulePreviewQuery query = new LocationRulePreviewQuery(
            50.08804,
            14.42076,
            200,
            MatchStart: true,
            MatchEnd: true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExcludeRuleId: null);

        string counts = LocationRuleService.BuildPreviewCountsQuery(context, Guid.NewGuid(), query).ToQueryString();
        string points = LocationRuleService.BuildPreviewPointsQuery(context, Guid.NewGuid(), query, 250).ToQueryString();

        foreach (string sql in new[] { counts, points })
        {
            // The same place test the journey rule uses, over the view, for one device
            // and the trips since the valid-from date — PostGIS, not a client-side filter.
            Assert.Contains("trip_effective_journeys", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("trip_near(", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ST_MakePoint(", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("v.device_id =", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("v.start_time >=", sql, StringComparison.OrdinalIgnoreCase);
        }

        // Totals are aggregated by the database; points are capped there.
        Assert.Contains("count(*) FILTER", counts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ST_DWithin(", points, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", points, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FixReadsAreFilteredOrderedAndCappedInSql()
    {
        using CarPosDbContext context = CreateContext();

        string sql = TripFixReader.BuildRangeQuery(
                context,
                Guid.NewGuid(),
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                fromInclusive: false,
                new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                TripFixReader.ChunkSize + 1)
            .ToQueryString();

        Assert.Contains("device_id =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fix_time >", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fix_time <=", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);

        // Only what the trip logic needs leaves the database — not altitude, battery or
        // the accelerometer.
        Assert.DoesNotContain("altitude_m", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("battery_pct", sql, StringComparison.OrdinalIgnoreCase);
    }
}
