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
