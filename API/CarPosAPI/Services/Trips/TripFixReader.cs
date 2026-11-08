using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ITripFixReader"/>. Every query walks the
/// <c>ux_positions_device_id_fix_time</c> index — device, then fix time — and is capped.
///
/// <para>
/// <b>Chunked analysis.</b> A trip between two starts can be a ten-minute drive or a
/// month of parking. Loading <see cref="ChunkSize"/> fixes at a time and asking the
/// analyzer whether it can decide yet means the common case costs one small query, and
/// only a trip that genuinely needs more fixes reads more. The analyzer stops at the
/// first confirmed stop, so a month of parking after a trip is never read in full.
/// </para>
/// </summary>
internal sealed class TripFixReader : ITripFixReader
{
    /// <summary>Fixes per query when rebuilding a trip.</summary>
    internal const int ChunkSize = 2_000;

    private readonly CarPosDbContext _context;

    /// <summary>Creates the reader.</summary>
    /// <param name="context">Scoped database context.</param>
    public TripFixReader(CarPosDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// The bounded, ordered fix query every read here is built on — exposed so a test can
    /// prove it runs in SQL.
    /// </summary>
    /// <param name="context">The context to query.</param>
    /// <param name="deviceRowId">The device.</param>
    /// <param name="from">Lower bound, or null for none.</param>
    /// <param name="fromInclusive">Whether the lower bound itself is included.</param>
    /// <param name="untilInclusive">Upper bound (inclusive), or null for none.</param>
    /// <param name="limit">Row cap.</param>
    /// <returns>The query.</returns>
    internal static IQueryable<TripPoint> BuildRangeQuery(
        CarPosDbContext context,
        Guid deviceRowId,
        DateTime? from,
        bool fromInclusive,
        DateTime? untilInclusive,
        int limit)
    {
        IQueryable<Data.Entities.Position> positions = context.Positions
            .AsNoTracking()
            .Where(position => position.DeviceId == deviceRowId);

        if (from is DateTime lower)
        {
            positions = fromInclusive
                ? positions.Where(position => position.FixTime >= lower)
                : positions.Where(position => position.FixTime > lower);
        }

        if (untilInclusive is DateTime upper)
        {
            positions = positions.Where(position => position.FixTime <= upper);
        }

        return positions
            .OrderBy(position => position.FixTime)
            .Take(limit)
            .Select(position => new TripPoint(position.FixTime, position.Latitude, position.Longitude, position.SpeedKmph));
    }

    /// <inheritdoc />
    public Task<List<TripPoint>> ReadFromAsync(
        Guid deviceRowId,
        DateTime? fromInclusive,
        int limit,
        CancellationToken cancellationToken) =>
        BuildRangeQuery(_context, deviceRowId, fromInclusive, fromInclusive: true, untilInclusive: null, limit)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<DateTime?> FirstFixAfterAsync(Guid deviceRowId, DateTime after, CancellationToken cancellationToken) =>
        _context.Positions
            .AsNoTracking()
            .Where(position => position.DeviceId == deviceRowId && position.FixTime > after)
            .OrderBy(position => position.FixTime)
            .Select(position => (DateTime?)position.FixTime)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<TripSegmentStats?> AnalyzeAsync(
        Guid deviceRowId,
        DateTime start,
        DateTime? nextStart,
        TripDetectionSettings settings,
        DateTime? nowUtc,
        int maxFixes,
        CancellationToken cancellationToken)
    {
        // The fix before the start: the last fix of the stop the trip left, whose leg
        // out of the parking spot is part of the trip.
        TripPoint? previous = await _context.Positions
            .AsNoTracking()
            .Where(position => position.DeviceId == deviceRowId && position.FixTime < start)
            .OrderByDescending(position => position.FixTime)
            .Select(position => new TripPoint(position.FixTime, position.Latitude, position.Longitude, position.SpeedKmph))
            .FirstOrDefaultAsync(cancellationToken);

        List<TripPoint> points = new List<TripPoint>();
        DateTime from = start;
        bool fromInclusive = true;
        DateTime? effectiveNow = nextStart is null ? nowUtc : null;

        while (true)
        {
            int room = Math.Min(ChunkSize, maxFixes - points.Count);
            if (room <= 0)
            {
                return null;
            }

            List<TripPoint> chunk = await BuildRangeQuery(_context, deviceRowId, from, fromInclusive, nextStart, room + 1)
                .ToListAsync(cancellationToken);
            bool moreDataAvailable = chunk.Count > room;
            if (moreDataAvailable)
            {
                chunk.RemoveAt(chunk.Count - 1);
            }

            points.AddRange(chunk);

            // The start fix is gone — erased since the trip was detected; the worker is
            // already marked to recompute this stretch.
            if (points.Count == 0 || points[0].FixTime != start)
            {
                return null;
            }

            bool lastPointStartsNextTrip = !moreDataAvailable
                && nextStart is DateTime next
                && points[^1].FixTime == next;

            TripSegmentStats? stats = TripSegmentAnalyzer.Analyze(
                previous,
                points,
                settings,
                effectiveNow,
                moreDataAvailable,
                lastPointStartsNextTrip);

            if (stats is not null || !moreDataAvailable)
            {
                return stats;
            }

            from = points[^1].FixTime;
            fromInclusive = false;
        }
    }
}
