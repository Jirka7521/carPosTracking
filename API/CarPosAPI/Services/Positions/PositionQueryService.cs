using CarPosAPI.Data;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Positions;

/// <summary>
/// Implements <see cref="IPositionQueryService"/> with a single indexed,
/// bounded query.
///
/// Filtering, ordering and the row cap all happen <em>in SQL</em>. That is the
/// difference between a query that touches a few hundred rows through
/// <c>ux_positions_device_id_fix_time</c> and one that drags a year of history
/// into memory to throw most of it away.
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class PositionQueryService : IPositionQueryService
{
    /// <summary>
    /// Hard ceiling on rows per query, matching the documented contract. A track
    /// long enough to need more than this is a track no browser can usefully draw.
    /// </summary>
    private const int MaxPositionsPerQuery = 1000;

    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    public PositionQueryService(CarPosDbContext context, IDeviceAccessAuthorizer authorizer)
    {
        _context = context;
        _authorizer = authorizer;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<PositionDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<IReadOnlyList<PositionDto>>.NotFound("No such device.");
        }

        // Every active grant carries CanRead, so reaching here is already
        // authorisation enough. Soft-deleted devices still return their history:
        // retiring a tracker must not erase where it has been.
        IQueryable<Data.Entities.Position> query = _context.Positions
            .AsNoTracking()
            .Where(position => position.DeviceId == access.DeviceRowId);

        if (fromUtc.HasValue)
        {
            DateTime from = NormaliseToUtc(fromUtc.Value);
            query = query.Where(position => position.FixTime >= from);
        }

        if (toUtc.HasValue)
        {
            DateTime to = NormaliseToUtc(toUtc.Value);
            query = query.Where(position => position.FixTime <= to);
        }

        List<PositionDto> positions = await query
            .OrderByDescending(position => position.FixTime)
            .Take(MaxPositionsPerQuery)
            .Select(position => new PositionDto(
                position.Id,
                access.DeviceId,
                position.FixTime,
                position.ReceivedAt,
                position.Latitude,
                position.Longitude,
                position.SpeedKmph,
                position.AltitudeMeters,
                position.BatteryPct,
                position.AccelXG,
                position.AccelYG,
                position.AccelZG,
                position.TemperatureC))
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<PositionDto>>.Success(positions);
    }

    /// <summary>
    /// Forces a query-string bound to <see cref="DateTimeKind.Utc"/>.
    ///
    /// <c>fix_time</c> is <c>timestamptz</c>, and Npgsql refuses any parameter for
    /// such a column whose kind is not UTC — so a bound arriving as Local (an ISO
    /// string with an offset) or Unspecified (one without) would throw at
    /// execution time rather than filter. Local values are <em>converted</em>, not
    /// re-labelled: "2026-07-22T10:00+02:00" has to compare as 08:00 UTC.
    /// </summary>
    /// <param name="value">A bound as model-bound from the query string.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>.</returns>
    private static DateTime NormaliseToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // No offset was sent. The API's convention is that a bare timestamp is
            // already UTC, so this only stamps the kind onto it.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
