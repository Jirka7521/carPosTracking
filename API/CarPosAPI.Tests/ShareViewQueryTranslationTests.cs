using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Tests;

/// <summary>
/// Proves the visitor's position query actually becomes SQL.
///
/// <para>
/// <see cref="Services.Sharing.ShareViewService"/> decides the three optional
/// fields inside the projection — <c>includeSpeed ? position.SpeedKmph : null</c>
/// — so that a value the share does not cover has no path to a response. That is
/// the right shape for the guarantee and a translation risk for EF: a conditional
/// producing a nullable from a non-nullable column is the kind of expression that
/// gets evaluated client-side, or refused outright, depending on the provider.
/// </para>
///
/// <para>
/// Either failure is invisible until a real visitor opens a real share, which is
/// the worst moment to find out. <c>ToQueryString()</c> compiles the query through
/// the actual Npgsql provider without opening a connection, so the check costs
/// nothing and needs no database.
/// </para>
/// </summary>
public sealed class ShareViewQueryTranslationTests
{
    /// <summary>
    /// Never connected to. The provider needs a well-formed string to build its
    /// SQL; <c>ToQueryString</c> stops short of executing anything.
    /// </summary>
    private const string UnusedConnectionString =
        "Host=localhost;Database=carpos_translation_check;Username=none;Password=none";

    private static CarPosDbContext CreateContext()
    {
        DbContextOptions<CarPosDbContext> options = new DbContextOptionsBuilder<CarPosDbContext>()
            .UseNpgsql(UnusedConnectionString)
            .Options;

        return new CarPosDbContext(options);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TheSharedProjectionTranslatesToSql(bool includeSpeed, bool includeTelemetry)
    {
        using CarPosDbContext context = CreateContext();

        Guid deviceRowId = Guid.NewGuid();
        DateTime from = new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);
        DateTime to = new DateTime(2026, 9, 12, 18, 0, 0, DateTimeKind.Utc);

        // The same shape as ShareViewService.QueryAsync. If that query changes, this
        // has to change with it — which is the point: the change is then made in the
        // open rather than discovered by a visitor.
        IQueryable<SharedPositionDto> query = context.Positions
            .AsNoTracking()
            .Where(position => position.DeviceId == deviceRowId)
            .Where(position => position.FixTime >= from && position.FixTime <= to)
            .OrderByDescending(position => position.FixTime)
            .Take(1000)
            .Select(position => new SharedPositionDto(
                position.FixTime,
                position.Latitude,
                position.Longitude,
                includeSpeed ? position.SpeedKmph : null,
                includeTelemetry ? position.BatteryPct : null,
                includeTelemetry ? position.TemperatureC : null));

        string sql = query.ToQueryString();

        // Bounded in SQL, not in memory: an unbounded read of this table is how the
        // API falls over, and a client-evaluated LIMIT is no limit at all.
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);

        // The columns the share does not cover must not be selected at all.
        AssertColumnPresence(sql, "speed_kmph", includeSpeed);
        AssertColumnPresence(sql, "battery_pct", includeTelemetry);
        AssertColumnPresence(sql, "temperature_c", includeTelemetry);

        // And the ones it never covers, whatever the flags say.
        Assert.DoesNotContain("altitude_m", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accel_x_g", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("received_at", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheShareLookupProjectionTranslatesToSql()
    {
        using CarPosDbContext context = CreateContext();

        Guid shareId = Guid.NewGuid();

        IQueryable<ShareLinkViewProbe> query = context.ShareLinks
            .AsNoTracking()
            .Where(candidate => candidate.Id == shareId)
            .Select(candidate => new ShareLinkViewProbe(
                candidate.DeviceId,
                candidate.Label,
                candidate.ValidFrom,
                candidate.ValidUntil,
                candidate.Scope,
                candidate.IncludeSpeed,
                candidate.IncludeTelemetry,
                candidate.RevokedAt));

        string sql = query.ToQueryString();

        // The three secret columns must not be read on a path that runs on every
        // refresh of a public page.
        Assert.DoesNotContain("verifier_hash", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase_hash", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selector", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Asserts a column is read exactly when the share covers it.</summary>
    /// <param name="sql">The compiled query.</param>
    /// <param name="column">The snake_case column name.</param>
    /// <param name="expected">Whether the share includes that field.</param>
    private static void AssertColumnPresence(string sql, string column, bool expected)
    {
        bool present = sql.Contains(column, StringComparison.OrdinalIgnoreCase);

        Assert.True(
            present == expected,
            expected
                ? $"{column} should be selected when the share includes it, but the SQL does not mention it."
                : $"{column} must not be selected when the share excludes it, but the SQL reads it.");
    }

    /// <summary>
    /// Stands in for the service's internal row type, which is not visible from the
    /// test assembly. Same shape, same columns — what is being checked is that the
    /// projection compiles to SQL, not the identity of the type it fills.
    /// </summary>
    private sealed record ShareLinkViewProbe(
        Guid DeviceId,
        string Label,
        DateTime ValidFrom,
        DateTime ValidUntil,
        ShareScope Scope,
        bool IncludeSpeed,
        bool IncludeTelemetry,
        DateTime? RevokedAt);
}
