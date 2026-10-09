using CarPosAPI.Data;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Devices;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Tests;

/// <summary>
/// Proves the two device-event reads run in the database, not in memory.
///
/// <para>
/// <c>device_events</c> grows by about one row per report on a device that sleeps
/// between reports, so it has the same failure mode as <c>positions</c>: a filter or a
/// cap that EF quietly evaluates client-side drags the whole history home. As in
/// <see cref="DeviceAccessCountsQueryTranslationTests"/>, <c>ToQueryString()</c> renders
/// the real Npgsql SQL without opening a connection.
/// </para>
/// </summary>
public sealed class DeviceEventQueryTranslationTests
{
    /// <summary>Never connected to — the provider only needs it to be well-formed.</summary>
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
    public void TheEventsListIsFilteredOrderedAndCappedInSql()
    {
        using CarPosDbContext context = CreateContext();

        // The real query, built by the service's own method — not a restatement of it.
        IQueryable<DeviceEventDto> query = DeviceEventQueryService.BuildQuery(
            context,
            Guid.NewGuid(),
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc),
            DeviceEventSeverityNames.AtLeast(DeviceEventSeverityNames.Alert),
            200);

        string sql = query.ToQueryString();

        Assert.Contains("device_events", sql, StringComparison.OrdinalIgnoreCase);
        // The severity filter, the time range, the order and the cap.
        Assert.Contains("severity", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("received_at >=", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("received_at <=", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheLatestOfflineEventIsASubqueryNotARoundTripPerDevice()
    {
        using CarPosDbContext context = CreateContext();

        int userId = 42;

        // The same shape as DeviceService.ListForUserAsync's badge projection, reduced
        // to the part under test. If that projection changes, this changes with it.
        IQueryable<DeviceOfflineEventDto?> query = context.Accesses
            .AsNoTracking()
            .Where(access => access.UserId == userId && access.IsActive)
            .Select(access => context.DeviceEvents
                .Where(deviceEvent => deviceEvent.DeviceId == access.DeviceId
                    && deviceEvent.Kind == DeviceEventKindNames.Offline)
                .OrderByDescending(deviceEvent => deviceEvent.ReceivedAt)
                .ThenByDescending(deviceEvent => deviceEvent.Id)
                .Select(deviceEvent => new DeviceOfflineEventDto(
                    deviceEvent.Reason,
                    deviceEvent.Severity,
                    deviceEvent.ReceivedAt,
                    deviceEvent.SleepSeconds))
                .FirstOrDefault());

        string sql = query.ToQueryString();

        // One statement that reaches both tables, with the "latest one" decided by the
        // database: a LIMIT inside, or a row-number window the provider may prefer.
        Assert.Contains("accesses", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("device_events", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("ROW_NUMBER", StringComparison.OrdinalIgnoreCase),
            $"The latest offline event must be picked in SQL:{Environment.NewLine}{sql}");
        Assert.Contains("'offline'", sql, StringComparison.OrdinalIgnoreCase);
    }
}
