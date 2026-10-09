using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Implements <see cref="IDeviceEventQueryService"/> with one indexed, bounded query.
///
/// <para>
/// Filtering, ordering and the row cap all run in SQL through
/// <c>ix_device_events_device_id_received_at</c>. The table grows by roughly one row
/// per report on a device that sleeps between reports, so it is in the same league as
/// <c>positions</c> and gets the same treatment: never read without a device and a cap.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class DeviceEventQueryService : IDeviceEventQueryService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    public DeviceEventQueryService(CarPosDbContext context, IDeviceAccessAuthorizer authorizer)
    {
        _context = context;
        _authorizer = authorizer;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<DeviceEventDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? minSeverity,
        int limit,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<IReadOnlyList<DeviceEventDto>>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        // Every active grant carries CanRead, and the history is no more sensitive
        // than the positions that grant already shows. Soft-deleted devices keep theirs.
        List<DeviceEventDto> events = await BuildQuery(
                _context,
                access.DeviceRowId,
                fromUtc.HasValue ? NormaliseToUtc(fromUtc.Value) : null,
                toUtc.HasValue ? NormaliseToUtc(toUtc.Value) : null,
                DeviceEventSeverityNames.AtLeast(minSeverity),
                limit)
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<DeviceEventDto>>.Success(events);
    }

    /// <summary>
    /// The whole query, filter to projection. Internal and static so
    /// <c>DeviceEventQueryTranslationTests</c> can render exactly this to SQL without a
    /// database and prove the severity filter and the cap run there.
    /// </summary>
    /// <param name="context">The context to query.</param>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="fromUtc">Inclusive lower bound (UTC kind), or null.</param>
    /// <param name="toUtc">Inclusive upper bound (UTC kind), or null.</param>
    /// <param name="severities">The severities to include.</param>
    /// <param name="limit">Most rows to return.</param>
    /// <returns>The query, not yet executed.</returns>
    internal static IQueryable<DeviceEventDto> BuildQuery(
        CarPosDbContext context,
        Guid deviceRowId,
        DateTime? fromUtc,
        DateTime? toUtc,
        IReadOnlyList<string> severities,
        int limit)
    {
        // A plain array: Npgsql binds it as one text[] parameter, so the filter is a
        // single "= ANY(@severities)" in SQL however many severities are allowed.
        string[] severityArray = severities.ToArray();

        IQueryable<DeviceEvent> query = context.DeviceEvents
            .AsNoTracking()
            .Where(deviceEvent => deviceEvent.DeviceId == deviceRowId
                && severityArray.Contains(deviceEvent.Severity));

        if (fromUtc.HasValue)
        {
            DateTime from = fromUtc.Value;
            query = query.Where(deviceEvent => deviceEvent.ReceivedAt >= from);
        }

        if (toUtc.HasValue)
        {
            DateTime to = toUtc.Value;
            query = query.Where(deviceEvent => deviceEvent.ReceivedAt <= to);
        }

        // Id breaks ties: an online message that records a restart and a sleep a second
        // later can share a receive time to the microsecond on a fast box, and the list
        // must not reorder itself between two refreshes.
        return query
            .OrderByDescending(deviceEvent => deviceEvent.ReceivedAt)
            .ThenByDescending(deviceEvent => deviceEvent.Id)
            .Take(limit)
            .Select(deviceEvent => new DeviceEventDto(
                deviceEvent.Id,
                deviceEvent.Kind,
                deviceEvent.Reason,
                deviceEvent.Severity,
                deviceEvent.ReceivedAt,
                deviceEvent.DeviceTime,
                deviceEvent.BatteryPct,
                deviceEvent.SleepSeconds,
                deviceEvent.Detail));
    }

    /// <summary>
    /// Forces a query-string bound to <see cref="DateTimeKind.Utc"/>, exactly as
    /// <see cref="Positions.PositionQueryService"/> does — <c>received_at</c> is
    /// <c>timestamptz</c>, and Npgsql refuses a parameter for it of any other kind.
    /// </summary>
    /// <param name="value">A bound as model-bound from the query string.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>.</returns>
    private static DateTime NormaliseToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
