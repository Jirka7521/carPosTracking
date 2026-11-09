using CarPosAPI.Data;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ITripReadService"/>. Trip rows come from
/// <see cref="TripQueries"/> (keyset-paged, journey computed in SQL); their statistics
/// from <see cref="ITripStatsCalculator"/>, within a fix budget per request so one huge
/// journey cannot hold a connection for long. Trips the budget did not reach come back
/// marked <c>statsPending</c> rather than slowing the response down.
///
/// <para>
/// Any active grant may read. A device the caller cannot see answers 404, never 403.
/// </para>
///
/// Scoped.
/// </summary>
internal sealed class TripReadService : ITripReadService
{
    /// <summary>Fix budget for one list page or one trip.</summary>
    internal const int MaxFixesPerList = 100_000;

    /// <summary>Fix budget for a range or journey summary.</summary>
    internal const int MaxFixesPerSummary = 500_000;

    /// <summary>The list query parameter meaning "every trip".</summary>
    internal const string AllJourneys = "all";

    /// <summary>The list query parameter meaning "trips in no journey".</summary>
    internal const string NoJourney = "none";

    // A dirty mark this old means older trips are being recomputed (new thresholds, or a
    // late offline backlog) — worth a banner. A fresh one is just the last save, cleared
    // within moments.
    private static readonly TimeSpan RecalculatingAfter = TimeSpan.FromHours(1);

    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ITripStatsCalculator _stats;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    /// <param name="stats">Rebuilds trips from positions.</param>
    public TripReadService(CarPosDbContext context, IDeviceAccessAuthorizer authorizer, ITripStatsCalculator stats)
    {
        _context = context;
        _authorizer = authorizer;
        _stats = stats;
    }

    /// <inheritdoc />
    public async Task<OperationResult<TripPageDto>> ListAsync(
        int userId,
        string deviceId,
        string? journey,
        DateTime? before,
        int limit,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<TripPageDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        IQueryable<TripRow> rows = TripQueries.Rows(_context, access.DeviceRowId);
        if (string.Equals(journey, NoJourney, StringComparison.Ordinal))
        {
            rows = rows.Where(row => row.EffectiveJourneyId == null);
        }
        else if (journey is not null && !string.Equals(journey, AllJourneys, StringComparison.Ordinal))
        {
            if (!Guid.TryParse(journey, out Guid journeyId)
                || !await JourneyExistsAsync(access.DeviceRowId, journeyId, cancellationToken))
            {
                return OperationResult<TripPageDto>.NotFound(ErrorCodes.NoSuchJourney, "No such journey.");
            }

            rows = rows.Where(row => row.EffectiveJourneyId == journeyId);
        }

        List<TripRow> page = await TripQueries
            .Page(rows, before.HasValue ? AsUtc(before.Value) : null, limit + 1)
            .ToListAsync(cancellationToken);

        bool hasMore = page.Count > limit;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        DateTime utcNow = DateTime.UtcNow;
        Dictionary<long, TripSegmentStats?> stats = await AnalyzeAsync(access.DeviceRowId, page, utcNow, MaxFixesPerList, cancellationToken);

        List<TripSummaryDto> items = page
            .Select(row => ToDto(row, stats.GetValueOrDefault(row.Id)))
            .ToList();

        return OperationResult<TripPageDto>.Success(new TripPageDto(
            items,
            hasMore ? page[^1].StartTime : null,
            await IsRecalculatingAsync(access.DeviceRowId, utcNow, cancellationToken)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<TripSummaryDto>> GetAsync(
        int userId,
        string deviceId,
        long tripId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<TripSummaryDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        TripRow? row = await TripQueries.Rows(_context, access.DeviceRowId)
            .Where(candidate => candidate.Id == tripId)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return OperationResult<TripSummaryDto>.NotFound(ErrorCodes.NoSuchTrip, "No such trip.");
        }

        Dictionary<long, TripSegmentStats?> stats = await AnalyzeAsync(
            access.DeviceRowId,
            new[] { row },
            DateTime.UtcNow,
            MaxFixesPerList,
            cancellationToken);

        return OperationResult<TripSummaryDto>.Success(ToDto(row, stats.GetValueOrDefault(row.Id)));
    }

    /// <inheritdoc />
    public async Task<OperationResult<TripRangeSummaryDto>> SummarizeRangeAsync(
        int userId,
        string deviceId,
        DateTime from,
        DateTime to,
        Guid? journeyId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<TripRangeSummaryDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        DateTime fromUtc = AsUtc(from);
        DateTime toUtc = AsUtc(to);
        if (fromUtc >= toUtc)
        {
            return OperationResult<TripRangeSummaryDto>.Invalid(ErrorCodes.JourneyRangeInvalid, "The range must end after it starts.");
        }

        List<TripRow> rows = await TripQueries.Rows(_context, access.DeviceRowId)
            .Where(row => row.StartTime >= fromUtc && row.StartTime < toUtc)
            .ToListAsync(cancellationToken);

        int pinnedElsewhere = rows.Count(row => row.JourneyPinned && row.EffectiveJourneyId != journeyId);
        TripTotals totals = await TotalsAsync(access.DeviceRowId, rows, cancellationToken);

        return OperationResult<TripRangeSummaryDto>.Success(new TripRangeSummaryDto(
            rows.Count,
            pinnedElsewhere,
            totals.Pending ? null : totals.DistanceMeters,
            totals.Pending ? null : totals.DurationSeconds,
            totals.Pending));
    }

    /// <inheritdoc />
    public async Task<OperationResult<JourneySummaryDto>> SummarizeJourneyAsync(
        int userId,
        string deviceId,
        Guid journeyId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<JourneySummaryDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        List<JourneySnapshot> journeys = await _context.Journeys
            .AsNoTracking()
            .Where(candidate => candidate.DeviceId == access.DeviceRowId)
            .Select(candidate => new JourneySnapshot(candidate.Id, candidate.ParentId, candidate.Name, candidate.RangeFrom, candidate.RangeTo))
            .ToListAsync(cancellationToken);
        if (!journeys.Any(candidate => candidate.Id == journeyId))
        {
            return OperationResult<JourneySummaryDto>.NotFound(ErrorCodes.NoSuchJourney, "No such journey.");
        }

        List<Guid> subtree = JourneyTreeRules.Subtree(journeyId, journeys).ToList();
        List<TripRow> rows = await TripQueries.Rows(_context, access.DeviceRowId)
            .Where(row => row.EffectiveJourneyId != null && subtree.Contains(row.EffectiveJourneyId.Value))
            .ToListAsync(cancellationToken);

        TripTotals totals = await TotalsAsync(access.DeviceRowId, rows, cancellationToken);

        return OperationResult<JourneySummaryDto>.Success(new JourneySummaryDto(
            rows.Count,
            totals.Pending ? null : totals.DistanceMeters,
            totals.Pending ? null : totals.DurationSeconds,
            totals.Pending ? null : totals.MovingSeconds,
            totals.Pending));
    }

    private async Task<Dictionary<long, TripSegmentStats?>> AnalyzeAsync(
        Guid deviceRowId,
        IReadOnlyList<TripRow> rows,
        DateTime utcNow,
        int maxFixes,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return new Dictionary<long, TripSegmentStats?>();
        }

        TripDetectionSettings settings = TripDetectionSettings.From(
            await _context.DeviceTripSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.DeviceId == deviceRowId, cancellationToken));

        List<TripSpan> spans = rows.Select(row => new TripSpan(row.Id, row.StartTime, row.NextStart)).ToList();
        return await _stats.AnalyzeAsync(deviceRowId, spans, settings, utcNow, maxFixes, cancellationToken);
    }

    private async Task<TripTotals> TotalsAsync(Guid deviceRowId, List<TripRow> rows, CancellationToken cancellationToken)
    {
        Dictionary<long, TripSegmentStats?> stats = await AnalyzeAsync(
            deviceRowId,
            rows,
            DateTime.UtcNow,
            MaxFixesPerSummary,
            cancellationToken);

        double distance = 0;
        double duration = 0;
        double moving = 0;
        bool pending = false;
        foreach (TripRow row in rows)
        {
            if (stats.GetValueOrDefault(row.Id) is TripSegmentStats trip)
            {
                distance += trip.DistanceMeters;
                duration += trip.DurationSeconds;
                moving += trip.MovingSeconds;
            }
            else
            {
                pending = true;
            }
        }

        return new TripTotals(distance, duration, moving, pending);
    }

    private Task<bool> JourneyExistsAsync(Guid deviceRowId, Guid journeyId, CancellationToken cancellationToken) =>
        _context.Journeys.AnyAsync(
            journey => journey.Id == journeyId && journey.DeviceId == deviceRowId,
            cancellationToken);

    private async Task<bool> IsRecalculatingAsync(Guid deviceRowId, DateTime utcNow, CancellationToken cancellationToken)
    {
        DateTime? dirtyFrom = await _context.TripDetectionStates
            .AsNoTracking()
            .Where(state => state.DeviceId == deviceRowId)
            .Select(state => state.DirtyFrom)
            .SingleOrDefaultAsync(cancellationToken);

        return dirtyFrom is DateTime mark && mark < utcNow - RecalculatingAfter;
    }

    private static TripSummaryDto ToDto(TripRow row, TripSegmentStats? stats) =>
        stats is null
            ? new TripSummaryDto(
                row.Id,
                row.StartTime,
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                null,
                row.EffectiveJourneyId,
                row.JourneyPinned,
                row.Name,
                StatsPending: true)
            : new TripSummaryDto(
                row.Id,
                row.StartTime,
                stats.Departure,
                stats.End,
                EndKindName(stats.EndKind),
                stats.InProgress,
                stats.DistanceMeters,
                stats.DurationSeconds,
                stats.MovingSeconds,
                stats.AverageSpeedKmph,
                stats.MovingAverageSpeedKmph,
                stats.MaxSpeedKmph,
                stats.HasGap,
                stats.PointCount,
                row.EffectiveJourneyId,
                row.JourneyPinned,
                row.Name,
                StatsPending: false);

    private static string EndKindName(TripEndKind kind) => kind switch
    {
        TripEndKind.Stay => TripEndKindNames.Stay,
        TripEndKind.Gap => TripEndKindNames.Gap,
        _ => TripEndKindNames.Open,
    };

    // Query-string instants arrive with whatever kind model binding gave them; the columns
    // are timestamptz and Npgsql insists on UTC — the same normalisation PositionQueryService uses.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
