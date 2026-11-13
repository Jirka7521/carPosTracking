using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ILocationRuleService"/>.
///
/// <para>
/// <b>Saving a rule never rewrites trips.</b> Which journey a trip is in is decided by the
/// <c>trip_effective_journeys</c> view at query time, so a new, edited or ended rule is
/// reflected by the next read, for past trips and late-arriving ones alike.
/// </para>
///
/// <para>
/// <b>The preview runs in the database</b>: <c>trip_near</c> (PostGIS <c>ST_DWithin</c> on
/// the trips' start, parking and end fixes) over the device's trips since the valid-from
/// date — the same function the view uses, so the preview counts exactly what the rule
/// will test.
/// </para>
///
/// <para>
/// Writes take <see cref="TripOrganisationLock"/>, like journey writes, so the per-device
/// caps hold and a journey being deleted cannot strand a rule saved at the same moment.
/// Logs carry ids and counts only — never coordinates or names.
/// </para>
///
/// Scoped.
/// </summary>
internal sealed class LocationRuleService : ILocationRuleService
{
    private const string StartKind = "start";
    private const string EndKind = "end";

    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ILogger<LocationRuleService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    /// <param name="logger">Structured logger — ids and counts only.</param>
    public LocationRuleService(CarPosDbContext context, IDeviceAccessAuthorizer authorizer, ILogger<LocationRuleService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _logger = logger;
    }

    /// <summary>
    /// The preview's counting query: one row of totals over the device's trips since the
    /// valid-from date that the circle catches. Exposed so a test can pin its SQL.
    /// </summary>
    /// <param name="context">The context to query.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="query">The rule being drawn; <c>ValidFrom</c> must be UTC.</param>
    /// <returns>The query.</returns>
    internal static IQueryable<LocationRulePreviewCounts> BuildPreviewCountsQuery(
        CarPosDbContext context,
        Guid deviceRowId,
        LocationRulePreviewQuery query)
    {
        double radius = query.RadiusMeters;
        Guid exclude = query.ExcludeRuleId ?? Guid.Empty;

        return context.Database.SqlQuery<LocationRulePreviewCounts>(
            $"""
             SELECT
                 count(*)::int AS "TripCount",
                 count(*) FILTER (WHERE m.by_start)::int AS "StartCount",
                 count(*) FILTER (WHERE m.by_end)::int AS "EndCount",
                 count(*) FILTER (WHERE v.journey_pinned)::int AS "PinnedCount",
                 count(*) FILTER (WHERE NOT v.journey_pinned
                                    AND v.location_rule_id IS NOT NULL
                                    AND v.location_rule_id <> {exclude})::int AS "OtherRuleCount"
             FROM trip_effective_journeys v
             CROSS JOIN (SELECT ST_SetSRID(ST_MakePoint({query.Longitude}, {query.Latitude}), 4326)::geography AS center) c
             CROSS JOIN LATERAL trip_near(v.device_id, v.start_time, v.end_time, c.center, {radius}) n
             CROSS JOIN LATERAL (SELECT ({query.MatchStart} AND n.at_start) AS by_start,
                                        ({query.MatchEnd} AND n.at_end) AS by_end) m
             WHERE v.device_id = {deviceRowId}
               AND v.start_time >= {query.ValidFrom}
               AND (m.by_start OR m.by_end)
             """);
    }

    /// <summary>
    /// The preview's points query: the newest matched trips with the fix that matched at
    /// each end — the start fix, or the parking fix when only that lay in the circle, and
    /// the end fix. Exposed so a test can pin its SQL.
    /// </summary>
    /// <param name="context">The context to query.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="query">The rule being drawn; <c>ValidFrom</c> must be UTC.</param>
    /// <param name="limit">Most trips to return.</param>
    /// <returns>The query.</returns>
    internal static IQueryable<LocationRulePreviewRow> BuildPreviewPointsQuery(
        CarPosDbContext context,
        Guid deviceRowId,
        LocationRulePreviewQuery query,
        int limit)
    {
        double radius = query.RadiusMeters;

        return context.Database.SqlQuery<LocationRulePreviewRow>(
            $"""
             SELECT
                 v.trip_id AS "TripId",
                 CASE WHEN m.by_start THEN COALESCE(sp.latitude, pk.latitude) END AS "StartLatitude",
                 CASE WHEN m.by_start THEN COALESCE(sp.longitude, pk.longitude) END AS "StartLongitude",
                 CASE WHEN m.by_end THEN ep.latitude END AS "EndLatitude",
                 CASE WHEN m.by_end THEN ep.longitude END AS "EndLongitude"
             FROM trip_effective_journeys v
             CROSS JOIN (SELECT ST_SetSRID(ST_MakePoint({query.Longitude}, {query.Latitude}), 4326)::geography AS center) c
             CROSS JOIN LATERAL trip_near(v.device_id, v.start_time, v.end_time, c.center, {radius}) n
             CROSS JOIN LATERAL (SELECT ({query.MatchStart} AND n.at_start) AS by_start,
                                        ({query.MatchEnd} AND n.at_end) AS by_end) m
             LEFT JOIN positions sp
                    ON m.by_start
                   AND sp.device_id = v.device_id
                   AND sp.fix_time = v.start_time
                   AND ST_DWithin(sp.location, c.center, {radius})
             LEFT JOIN LATERAL (
                    SELECT pp.latitude, pp.longitude
                    FROM positions pp
                    WHERE m.by_start
                      AND pp.device_id = v.device_id
                      AND pp.fix_time < v.start_time
                    ORDER BY pp.fix_time DESC
                    LIMIT 1) pk ON true
             LEFT JOIN positions ep
                    ON m.by_end
                   AND ep.device_id = v.device_id
                   AND ep.fix_time = v.end_time
             WHERE v.device_id = {deviceRowId}
               AND v.start_time >= {query.ValidFrom}
               AND (m.by_start OR m.by_end)
             ORDER BY v.start_time DESC
             LIMIT {limit}
             """);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<LocationRuleDto>>> ListAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<IReadOnlyList<LocationRuleDto>>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        List<JourneyLocationRule> rules = await _context.JourneyLocationRules
            .AsNoTracking()
            .Where(rule => rule.DeviceId == access.DeviceRowId)
            .ToListAsync(cancellationToken);
        Dictionary<Guid, int> counts = await CountTripsAsync(access.DeviceRowId, null, cancellationToken);

        List<LocationRuleDto> dtos = rules
            .OrderBy(rule => rule.EndedAt is not null)
            .ThenByDescending(rule => rule.EndedAt ?? rule.CreatedAt)
            .ThenByDescending(rule => rule.CreatedAt)
            .Select(rule => ToDto(rule, counts.GetValueOrDefault(rule.Id)))
            .ToList();

        return OperationResult<IReadOnlyList<LocationRuleDto>>.Success(dtos);
    }

    /// <inheritdoc />
    public async Task<OperationResult<LocationRulePreviewDto>> PreviewAsync(
        int userId,
        string deviceId,
        LocationRulePreviewQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<LocationRulePreviewDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!query.MatchStart && !query.MatchEnd)
        {
            return InvalidMatch<LocationRulePreviewDto>();
        }

        LocationRulePreviewQuery utcQuery = query with { ValidFrom = AsUtc(query.ValidFrom) };

        LocationRulePreviewCounts counts = await BuildPreviewCountsQuery(_context, access.DeviceRowId, utcQuery)
            .SingleAsync(cancellationToken);

        // Up to two points per trip (start and end), so half as many trips as points.
        int tripLimit = TripRules.MaxLocationRulePreviewPoints / 2;
        List<LocationRulePreviewRow> rows = counts.TripCount == 0
            ? new List<LocationRulePreviewRow>()
            : await BuildPreviewPointsQuery(_context, access.DeviceRowId, utcQuery, tripLimit)
                .ToListAsync(cancellationToken);

        List<LocationRulePreviewPointDto> points = new List<LocationRulePreviewPointDto>();
        foreach (LocationRulePreviewRow row in rows)
        {
            if (row.StartLatitude is double startLatitude && row.StartLongitude is double startLongitude)
            {
                points.Add(new LocationRulePreviewPointDto(row.TripId, StartKind, startLatitude, startLongitude));
            }

            if (row.EndLatitude is double endLatitude && row.EndLongitude is double endLongitude)
            {
                points.Add(new LocationRulePreviewPointDto(row.TripId, EndKind, endLatitude, endLongitude));
            }
        }

        return OperationResult<LocationRulePreviewDto>.Success(new LocationRulePreviewDto(
            counts.TripCount,
            counts.StartCount,
            counts.EndCount,
            counts.PinnedCount,
            counts.OtherRuleCount,
            points,
            PointsTruncated: counts.TripCount > rows.Count));
    }

    /// <inheritdoc />
    public Task<OperationResult<LocationRuleDto>> CreateAsync(
        int userId,
        string deviceId,
        LocationRuleSaveRequestDto request,
        CancellationToken cancellationToken) =>
        SaveAsync(userId, deviceId, null, request, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<LocationRuleDto>> UpdateAsync(
        int userId,
        string deviceId,
        Guid ruleId,
        LocationRuleSaveRequestDto request,
        CancellationToken cancellationToken) =>
        SaveAsync(userId, deviceId, ruleId, request, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<LocationRuleDto>> EndAsync(
        int userId,
        string deviceId,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<LocationRuleDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return Forbidden<LocationRuleDto>();
        }

        Guid deviceRowId = access.DeviceRowId;
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        OperationResult<LocationRuleDto> ended = await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(attemptToken);
                await TripOrganisationLock.AcquireAsync(_context, deviceRowId, attemptToken);

                JourneyLocationRule? rule = await _context.JourneyLocationRules.SingleOrDefaultAsync(
                    candidate => candidate.Id == ruleId && candidate.DeviceId == deviceRowId,
                    attemptToken);
                if (rule is null)
                {
                    return NoSuchRule();
                }

                if (rule.EndedAt is not null)
                {
                    return AlreadyEnded();
                }

                DateTime now = DateTime.UtcNow;
                rule.EndedAt = now;
                rule.EndReason = LocationRuleEndReasons.User;
                rule.UpdatedAt = now;
                await _context.SaveChangesAsync(attemptToken);
                await transaction.CommitAsync(attemptToken);

                return OperationResult<LocationRuleDto>.Success(ToDto(rule, 0));
            },
            cancellationToken);

        if (!ended.IsSuccess)
        {
            return ended;
        }

        _logger.LogInformation("User {UserId} ended location rule {RuleId} on device {DeviceId}", userId, ruleId, access.DeviceId);
        return await WithTripCountAsync(deviceRowId, ended.Value!, cancellationToken);
    }

    private async Task<OperationResult<LocationRuleDto>> SaveAsync(
        int userId,
        string deviceId,
        Guid? ruleId,
        LocationRuleSaveRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<LocationRuleDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return Forbidden<LocationRuleDto>();
        }

        if (!request.MatchStart && !request.MatchEnd)
        {
            return InvalidMatch<LocationRuleDto>();
        }

        // [Required] on both, so the binder has already refused a missing value.
        Guid journeyId = request.JourneyId!.Value;
        DateTime validFrom = AsUtc(request.ValidFrom!.Value);
        string? name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();

        Guid deviceRowId = access.DeviceRowId;
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        OperationResult<LocationRuleDto> saved = await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(attemptToken);
                await TripOrganisationLock.AcquireAsync(_context, deviceRowId, attemptToken);

                bool journeyExists = await _context.Journeys.AnyAsync(
                    journey => journey.Id == journeyId && journey.DeviceId == deviceRowId,
                    attemptToken);
                if (!journeyExists)
                {
                    return OperationResult<LocationRuleDto>.NotFound(ErrorCodes.NoSuchJourney, "No such journey.");
                }

                JourneyLocationRule? rule;
                if (ruleId is Guid existingId)
                {
                    rule = await _context.JourneyLocationRules.SingleOrDefaultAsync(
                        candidate => candidate.Id == existingId && candidate.DeviceId == deviceRowId,
                        attemptToken);
                    if (rule is null)
                    {
                        return NoSuchRule();
                    }

                    if (rule.EndedAt is not null)
                    {
                        return AlreadyEnded();
                    }
                }
                else
                {
                    OperationResult<LocationRuleDto>? full = await CheckCapsAsync(deviceRowId, attemptToken);
                    if (full is not null)
                    {
                        return full;
                    }

                    rule = new JourneyLocationRule { DeviceId = deviceRowId, CreatedAt = DateTime.UtcNow };
                    _context.JourneyLocationRules.Add(rule);
                }

                rule.JourneyId = journeyId;
                rule.Name = name;
                rule.Latitude = request.Latitude;
                rule.Longitude = request.Longitude;
                rule.RadiusMeters = request.RadiusMeters;
                rule.MatchStart = request.MatchStart;
                rule.MatchEnd = request.MatchEnd;
                rule.ValidFrom = validFrom;
                rule.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(attemptToken);
                await transaction.CommitAsync(attemptToken);

                return OperationResult<LocationRuleDto>.Success(ToDto(rule, 0));
            },
            cancellationToken);

        if (!saved.IsSuccess)
        {
            return saved;
        }

        _logger.LogInformation(
            "User {UserId} {Action} location rule {RuleId} on device {DeviceId}",
            userId,
            ruleId is null ? "created" : "updated",
            saved.Value!.Id,
            access.DeviceId);

        return await WithTripCountAsync(deviceRowId, saved.Value, cancellationToken);
    }

    // The per-device caps, checked under the lock: rules in force (they cost every trip
    // read) and rules in all (the history must not grow without bound).
    private async Task<OperationResult<LocationRuleDto>?> CheckCapsAsync(Guid deviceRowId, CancellationToken cancellationToken)
    {
        int total = await _context.JourneyLocationRules.CountAsync(rule => rule.DeviceId == deviceRowId, cancellationToken);
        if (total >= TripRules.MaxLocationRulesPerDevice)
        {
            return TooMany(TripRules.MaxLocationRulesPerDevice);
        }

        int active = await _context.JourneyLocationRules.CountAsync(
            rule => rule.DeviceId == deviceRowId && rule.EndedAt == null,
            cancellationToken);
        return active >= TripRules.MaxActiveLocationRulesPerDevice
            ? TooMany(TripRules.MaxActiveLocationRulesPerDevice)
            : null;
    }

    // Trips each rule places right now, counted by the database from the view. Null for
    // every rule of the device, or one rule's id.
    private Task<Dictionary<Guid, int>> CountTripsAsync(Guid deviceRowId, Guid? ruleId, CancellationToken cancellationToken)
    {
        IQueryable<TripEffectiveJourney> rows = _context.TripEffectiveJourneys
            .Where(row => row.DeviceId == deviceRowId && row.LocationRuleId != null);
        if (ruleId is Guid only)
        {
            rows = rows.Where(row => row.LocationRuleId == only);
        }

        return rows
            .GroupBy(row => row.LocationRuleId!.Value)
            .Select(group => new LocationRuleTripCount(group.Key, group.Count()))
            .ToDictionaryAsync(count => count.RuleId, count => count.TripCount, cancellationToken);
    }

    private async Task<OperationResult<LocationRuleDto>> WithTripCountAsync(
        Guid deviceRowId,
        LocationRuleDto rule,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, int> counts = await CountTripsAsync(deviceRowId, rule.Id, cancellationToken);
        return OperationResult<LocationRuleDto>.Success(rule with { TripCount = counts.GetValueOrDefault(rule.Id) });
    }

    private static LocationRuleDto ToDto(JourneyLocationRule rule, int tripCount) =>
        new LocationRuleDto(
            rule.Id,
            rule.JourneyId,
            rule.Name,
            rule.Latitude,
            rule.Longitude,
            rule.RadiusMeters,
            rule.MatchStart,
            rule.MatchEnd,
            rule.ValidFrom,
            rule.EndedAt,
            rule.EndReason,
            rule.CreatedAt,
            rule.UpdatedAt,
            tripCount);

    private static OperationResult<LocationRuleDto> NoSuchRule() =>
        OperationResult<LocationRuleDto>.NotFound(ErrorCodes.NoSuchLocationRule, "No such location rule.");

    private static OperationResult<LocationRuleDto> AlreadyEnded() =>
        OperationResult<LocationRuleDto>.Conflict(
            ErrorCodes.LocationRuleEnded,
            "This location rule has ended and is kept as history; create a new one instead.");

    private static OperationResult<LocationRuleDto> TooMany(int max) =>
        OperationResult<LocationRuleDto>.Conflict(
            ErrorCodes.TooManyLocationRules,
            "This device already has as many location rules as allowed.",
            new Dictionary<string, object> { ["max"] = max });

    private static OperationResult<T> InvalidMatch<T>() =>
        OperationResult<T>.Invalid(
            ErrorCodes.LocationRuleInvalid,
            "A location rule must match where trips start, where they end, or both.");

    private static OperationResult<T> Forbidden<T>() =>
        OperationResult<T>.Forbidden(
            ErrorCodes.NoPermissionManageTrips,
            "You do not have permission to organise this device's trips.");

    // Instants arrive with whatever kind model binding gave them; the columns are
    // timestamptz and Npgsql insists on UTC.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
