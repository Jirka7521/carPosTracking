using CarPosAPI.Data;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Tests;

/// <summary>
/// Proves the two access counts on the device list are counted by the database.
///
/// <para>
/// <see cref="Services.Devices.DeviceService.ListForUserAsync"/> builds both
/// figures as correlated subqueries inside its projection, and its own comment
/// promises the whole list stays a single round trip. That promise is exactly the
/// kind EF Core can break silently: an expression it declines to translate is
/// evaluated client-side instead, which here would mean fetching every grant and
/// every share link for every device and counting them in memory — the textbook
/// N+1, arriving without an error message.
/// </para>
///
/// <para>
/// So the check is not "does it compile" but "is COUNT in the SQL". As in
/// <see cref="ShareViewQueryTranslationTests"/>, <c>ToQueryString()</c> runs the
/// query through the real Npgsql provider without opening a connection, so this
/// needs no database.
/// </para>
/// </summary>
public sealed class DeviceAccessCountsQueryTranslationTests
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
    public void TheAccessCountsAreCountedInSql()
    {
        using CarPosDbContext context = CreateContext();

        int userId = 42;
        DateTime nowUtc = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

        // The same shape as ListForUserAsync, reduced to the part under test. If that
        // projection changes, this has to change with it — which is the point: the
        // change is then made deliberately rather than found in production latency.
        IQueryable<DeviceAccessCountsDto> query = context.Accesses
            .AsNoTracking()
            .Where(access => access.UserId == userId && access.IsActive)
            .Select(access => new DeviceAccessCountsDto(
                context.Accesses
                    .Count(other => other.DeviceId == access.DeviceId && other.IsActive),
                context.ShareLinks
                    .Count(link => link.DeviceId == access.DeviceId
                        && link.RevokedAt == null
                        && link.ValidFrom <= nowUtc
                        && link.ValidUntil >= nowUtc)));

        string sql = query.ToQueryString();

        // Two aggregates, both in the database. Anything less means one of them came
        // home as rows.
        int countOccurrences = sql.Split("count(", StringSplitOptions.None).Length - 1;
        Assert.True(
            countOccurrences >= 2,
            $"Both access counts must be aggregated in SQL, but the query contains "
                + $"{countOccurrences} count(...) call(s):{Environment.NewLine}{sql}");

        Assert.Contains("accesses", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("share_links", sql, StringComparison.OrdinalIgnoreCase);

        // The live-window filter has to be in the SQL too. Counting every link and
        // filtering afterwards would report revoked and expired shares as current.
        Assert.Contains("revoked_at", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid_from", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid_until", sql, StringComparison.OrdinalIgnoreCase);

        // A link's secrets have no business in a query that runs for every card in
        // the device grid; counting rows must never read their columns.
        Assert.DoesNotContain("verifier", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase", sql, StringComparison.OrdinalIgnoreCase);
    }
}
