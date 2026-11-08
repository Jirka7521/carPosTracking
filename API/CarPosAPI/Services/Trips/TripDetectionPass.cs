using System.Diagnostics;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ITripDetectionPass"/>: brings stored trip starts in line with
/// stored positions.
///
/// <para>
/// <b>1. Find what is new.</b> Positions above the cursor are summarised per device and
/// folded into each device's <c>dirty_from</c>, and the cursor advances — one
/// transaction, so a crash between the two cannot lose a device's mark. This works only
/// because <c>PositionWriter</c> is the single writer of positions, in one process, one
/// batch at a time: identity values commit in order and an id scan cannot skip a row.
/// </para>
///
/// <para>
/// <b>2. Recompute only the window that changed.</b> For a dirty device the scan restarts
/// at a restart point before <c>dirty_from</c> — where the trip before the affected one
/// ended — which <see cref="TripDetector"/> guarantees reproduces everything before it.
/// Fixes are loaded in bounded windows; a window cut short resumes at the detector's
/// restart point, so a full-history backfill proceeds in slices without ever holding the
/// history in memory.
/// </para>
///
/// <para>
/// <b>3. Apply under the device's lock.</b> The device's <c>trip_detection_state</c> row is
/// locked first. If it is gone (account erased) or its epoch moved (positions erased,
/// thresholds changed), the result is stale and discarded. Otherwise the affected trips
/// are locked and <see cref="TripMetadataMatcher"/> updates rows in place, so trip ids,
/// names and manual placements survive. Lock order is always state row, then trips, in
/// every writer, so nothing can deadlock.
/// </para>
///
/// Scoped; opened by <see cref="TripDetectionWorker"/> once per pass.
/// </summary>
internal sealed class TripDetectionPass : ITripDetectionPass
{
    /// <summary>Most new positions one pass folds into dirty marks; the rest wait one pass.</summary>
    internal const int ArrivalScanLimit = 50_000;

    /// <summary>Fixes per recompute window (about a month of a busy car's history).</summary>
    internal const int InitialWindowFixes = 20_000;

    /// <summary>
    /// The largest window tried when a window holds no restart point at all — a car parked
    /// for most of a year. Past this the device is reported as failing rather than read
    /// in ever larger pieces.
    /// </summary>
    internal const int MaxWindowFixes = 320_000;

    /// <summary>Most fixes read to rebuild the trip before a restart point.</summary>
    internal const int MaxRestartAnalysisFixes = 200_000;

    /// <summary>How long one pass may spend recomputing before it yields to the next.</summary>
    internal static readonly TimeSpan PassTimeBudget = TimeSpan.FromSeconds(20);

    // Bounds for "the whole history" in the locking query: no GNSS fix predates 1970, and
    // none postdates the year 9999.
    private static readonly DateTime BeforeAllFixes = DateTime.UnixEpoch;
    private static readonly DateTime AfterAllFixes = new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly CarPosDbContext _context;
    private readonly ITripFixReader _fixes;

    /// <summary>Creates the pass.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="fixes">Bounded fix reads and trip rebuilding.</param>
    public TripDetectionPass(CarPosDbContext context, ITripFixReader fixes)
    {
        _context = context;
        _fixes = fixes;
    }

    /// <summary>
    /// The per-device summary of positions above the cursor, capped and grouped in SQL —
    /// exposed so a test can prove it never drags rows home.
    /// </summary>
    /// <param name="context">The context to query.</param>
    /// <param name="afterPositionId">The cursor.</param>
    /// <param name="limit">Most position rows to look at.</param>
    /// <returns>The query.</returns>
    internal static IQueryable<PositionArrival> BuildArrivalQuery(CarPosDbContext context, long afterPositionId, int limit) =>
        context.Positions
            .AsNoTracking()
            .Where(position => position.Id > afterPositionId)
            .OrderBy(position => position.Id)
            .Take(limit)
            .GroupBy(position => position.DeviceId)
            .Select(group => new PositionArrival(
                group.Key,
                group.Min(position => position.FixTime),
                group.Max(position => position.Id),
                group.Count()));

    /// <inheritdoc />
    public async Task<TripPassOutcome> RunAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        bool moreArrivals = await MarkArrivalsAsync(cancellationToken);

        // Least recently served first (never-served before all), so one device with a
        // long backfill cannot starve the others of their live updates.
        List<Guid> dirtyDevices = await _context.TripDetectionStates
            .AsNoTracking()
            .Where(state => state.DirtyFrom != null)
            .OrderBy(state => state.LastPassAt != null)
            .ThenBy(state => state.LastPassAt)
            .Select(state => state.DeviceId)
            .ToListAsync(cancellationToken);

        int processed = 0;
        int tripsChanged = 0;
        bool moreDirty = false;

        foreach (Guid deviceRowId in dirtyDevices)
        {
            if (elapsed.Elapsed >= PassTimeBudget)
            {
                moreDirty = true;
                break;
            }

            DeviceRecomputeOutcome outcome = await RecomputeDeviceAsync(deviceRowId, utcNow, elapsed, cancellationToken);
            processed++;
            tripsChanged += outcome.TripsChanged;
            moreDirty |= !outcome.Finished;
        }

        return new TripPassOutcome(processed, tripsChanged, moreArrivals || moreDirty);
    }

    // Step 1: fold new positions into dirty marks and advance the cursor, atomically.
    private async Task<bool> MarkArrivalsAsync(CancellationToken cancellationToken)
    {
        long cursor = await _context.TripDetectionCursors
            .AsNoTracking()
            .Where(row => row.Id == TripDetectionCursor.SingletonId)
            .Select(row => row.LastPositionId)
            .SingleAsync(cancellationToken);

        List<PositionArrival> arrivals = await BuildArrivalQuery(_context, cursor, ArrivalScanLimit)
            .ToListAsync(cancellationToken);
        if (arrivals.Count == 0)
        {
            return false;
        }

        long newCursor = arrivals.Max(arrival => arrival.LastPositionId);

        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                await using IDbContextTransaction transaction =
                    await _context.Database.BeginTransactionAsync(attemptToken);

                foreach (PositionArrival arrival in arrivals)
                {
                    await TripDirtyMarker.MarkAsync(
                        _context,
                        arrival.DeviceId,
                        arrival.EarliestFixTime,
                        invalidateInFlight: false,
                        attemptToken);
                }

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE trip_detection_cursor SET last_position_id = GREATEST(last_position_id, {newCursor}) WHERE id = {TripDetectionCursor.SingletonId}",
                    attemptToken);

                await transaction.CommitAsync(attemptToken);
            },
            cancellationToken);

        return arrivals.Sum(arrival => arrival.Count) >= ArrivalScanLimit;
    }

    // Steps 2 and 3 for one device, window after window until done or out of time.
    private async Task<DeviceRecomputeOutcome> RecomputeDeviceAsync(
        Guid deviceRowId,
        DateTime utcNow,
        Stopwatch elapsed,
        CancellationToken cancellationToken)
    {
        TripDetectionState? state = await _context.TripDetectionStates
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.DeviceId == deviceRowId, cancellationToken);
        if (state?.DirtyFrom is not DateTime dirtyFrom)
        {
            return new DeviceRecomputeOutcome(0, Finished: true);
        }

        TripDetectionSettings settings = TripDetectionSettings.From(
            await _context.DeviceTripSettings
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.DeviceId == deviceRowId, cancellationToken));

        DateTime? restart = await FindRestartPointAsync(deviceRowId, dirtyFrom, settings, cancellationToken);
        int tripsChanged = 0;

        while (true)
        {
            TripWindow window = await DetectWindowAsync(deviceRowId, restart, settings, cancellationToken);

            int? applied = await ApplyAsync(deviceRowId, state.Epoch, window, utcNow, cancellationToken);
            if (applied is null)
            {
                // Invalidated while computing: the device is dirty again (or gone).
                return new DeviceRecomputeOutcome(tripsChanged, Finished: false);
            }

            tripsChanged += applied.Value;
            if (window.IsComplete)
            {
                return new DeviceRecomputeOutcome(tripsChanged, Finished: true);
            }

            restart = window.ResumeFrom;
            if (elapsed.Elapsed >= PassTimeBudget)
            {
                return new DeviceRecomputeOutcome(tripsChanged, Finished: false);
            }
        }
    }

    // The restart point before the earliest change: where the trip before the latest trip
    // that the change cannot have touched ended. Null means "from the first fix".
    private async Task<DateTime?> FindRestartPointAsync(
        Guid deviceRowId,
        DateTime dirtyFrom,
        TripDetectionSettings settings,
        CancellationToken cancellationToken)
    {
        // A change at dirtyFrom can reshape the stop it falls in, which began at most one
        // minimum stop earlier; trips starting before that are untouched.
        DateTime untouchedBefore = dirtyFrom.AddSeconds(-settings.MinStopSeconds);

        DateTime? anchorStart = await _context.Trips
            .AsNoTracking()
            .Where(trip => trip.DeviceId == deviceRowId && trip.StartTime <= untouchedBefore)
            .OrderByDescending(trip => trip.StartTime)
            .Select(trip => (DateTime?)trip.StartTime)
            .FirstOrDefaultAsync(cancellationToken);
        if (anchorStart is null)
        {
            return null;
        }

        DateTime? previousStart = await _context.Trips
            .AsNoTracking()
            .Where(trip => trip.DeviceId == deviceRowId && trip.StartTime < anchorStart.Value)
            .OrderByDescending(trip => trip.StartTime)
            .Select(trip => (DateTime?)trip.StartTime)
            .FirstOrDefaultAsync(cancellationToken);
        if (previousStart is null)
        {
            return null;
        }

        TripSegmentStats? previous = await _fixes.AnalyzeAsync(
            deviceRowId,
            previousStart.Value,
            anchorStart.Value,
            settings,
            nowUtc: null,
            MaxRestartAnalysisFixes,
            cancellationToken);

        // The stored starts no longer agree with the fixes: recompute everything rather
        // than trust a restart point derived from them.
        if (previous is null || previous.EndKind == TripEndKind.Open)
        {
            return null;
        }

        return previous.EndKind == TripEndKind.Stay
            ? previous.End
            : await _fixes.FirstFixAfterAsync(deviceRowId, previous.End, cancellationToken);
    }

    // Loads a window from the restart point and detects in it, widening the window only
    // when it holds no later restart point to resume from.
    private async Task<TripWindow> DetectWindowAsync(
        Guid deviceRowId,
        DateTime? restart,
        TripDetectionSettings settings,
        CancellationToken cancellationToken)
    {
        int budget = InitialWindowFixes;

        while (true)
        {
            List<TripPoint> points = await _fixes.ReadFromAsync(deviceRowId, restart, budget + 1, cancellationToken);
            bool moreDataAvailable = points.Count > budget;
            if (moreDataAvailable)
            {
                points.RemoveAt(points.Count - 1);
            }

            TripDetectionResult result = TripDetector.Detect(points, settings, moreDataAvailable);
            if (result.IsComplete)
            {
                return new TripWindow(restart, result.Starts, null, IsComplete: true);
            }

            if (result.ResumeFrom is DateTime resume && (restart is null || resume > restart.Value))
            {
                return new TripWindow(restart, result.Starts, resume, IsComplete: false);
            }

            if (budget >= MaxWindowFixes)
            {
                throw new InvalidOperationException(
                    $"No trip restart point within {budget} fixes; the device's trips cannot be recomputed in bounded windows.");
            }

            budget *= 4;
        }
    }

    // Replaces the window's trips under the device lock. Null when the result went stale.
    private async Task<int?> ApplyAsync(
        Guid deviceRowId,
        int epoch,
        TripWindow window,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        DateTime lower = window.From ?? BeforeAllFixes;
        DateTime upper = window.ResumeFrom ?? AfterAllFixes;

        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                // A replayed attempt must not see the entities the failed one added.
                _context.ChangeTracker.Clear();

                await using IDbContextTransaction transaction =
                    await _context.Database.BeginTransactionAsync(attemptToken);

                TripDetectionState? locked = await _context.TripDetectionStates
                    .FromSql($"SELECT * FROM trip_detection_state WHERE device_id = {deviceRowId} FOR UPDATE")
                    .SingleOrDefaultAsync(attemptToken);
                if (locked is null || locked.Epoch != epoch)
                {
                    return (int?)null;
                }

                List<Trip> existing = await _context.Trips
                    .FromSql($"SELECT * FROM trips WHERE device_id = {deviceRowId} AND start_time >= {lower} AND start_time < {upper} FOR UPDATE")
                    .ToListAsync(attemptToken);

                List<StoredTripSnapshot> snapshots = existing
                    .Select(trip => new StoredTripSnapshot(trip.Id, trip.StartTime, trip.JourneyId, trip.JourneyPinned, trip.Name))
                    .ToList();
                TripMatchResult match = TripMetadataMatcher.Match(snapshots, window.Starts);

                Dictionary<long, Trip> byId = existing.ToDictionary(trip => trip.Id);
                int changed = 0;

                foreach (TripMatch tripMatch in match.Matches)
                {
                    if (tripMatch.ExistingTripId is long existingId)
                    {
                        Trip row = byId[existingId];
                        if (row.StartTime != tripMatch.StartTime)
                        {
                            row.StartTime = tripMatch.StartTime;
                            row.ComputedAt = utcNow;
                            changed++;
                        }
                    }
                    else
                    {
                        _context.Trips.Add(new Trip
                        {
                            DeviceId = deviceRowId,
                            StartTime = tripMatch.StartTime,
                            ComputedAt = utcNow,
                        });
                        changed++;
                    }
                }

                foreach (long removedId in match.RemovedTripIds)
                {
                    _context.Trips.Remove(byId[removedId]);
                    changed++;
                }

                locked.DirtyFrom = window.IsComplete ? null : window.ResumeFrom;
                locked.LastPassAt = utcNow;

                await _context.SaveChangesAsync(attemptToken);
                await transaction.CommitAsync(attemptToken);

                return (int?)changed;
            },
            cancellationToken);
    }
}
