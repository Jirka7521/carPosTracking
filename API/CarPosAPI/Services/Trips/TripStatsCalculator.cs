namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ITripStatsCalculator"/> with <b>windowed bulk reads</b>.
///
/// <para>
/// Rebuilding fifty trips one by one would cost a hundred round trips to a remote
/// database. Instead the trips are taken oldest first and fixes are read in windows of
/// up to <see cref="WindowFixes"/> starting at the first trip not yet rebuilt; every trip
/// the window lets <see cref="TripSegmentAnalyzer"/> decide is rebuilt in memory, and the
/// next window starts at the first one it could not. A list page of recent trips is
/// usually one query. A trip that alone needs more than a window (a very long drive) is
/// rebuilt on its own, chunk by chunk.
/// </para>
///
/// <para>
/// Every read is bounded by device, time and row count, and the call as a whole by
/// <c>maxFixes</c>; trips left over when it runs out come back as null ("pending").
/// </para>
/// </summary>
internal sealed class TripStatsCalculator : ITripStatsCalculator
{
    /// <summary>Fixes per bulk read.</summary>
    internal const int WindowFixes = 25_000;

    private readonly ITripFixReader _fixes;
    private readonly int _windowFixes;

    /// <summary>Creates the calculator.</summary>
    /// <param name="fixes">Bounded fix reads.</param>
    public TripStatsCalculator(ITripFixReader fixes)
        : this(fixes, WindowFixes)
    {
    }

    /// <summary>Creates the calculator with a custom window — tests use tiny ones to force many windows.</summary>
    /// <param name="fixes">Bounded fix reads.</param>
    /// <param name="windowFixes">Fixes per bulk read.</param>
    internal TripStatsCalculator(ITripFixReader fixes, int windowFixes)
    {
        _fixes = fixes;
        _windowFixes = windowFixes;
    }

    /// <inheritdoc />
    public async Task<Dictionary<long, TripSegmentStats?>> AnalyzeAsync(
        Guid deviceRowId,
        IReadOnlyList<TripSpan> spans,
        TripDetectionSettings settings,
        DateTime nowUtc,
        int maxFixes,
        CancellationToken cancellationToken)
    {
        Dictionary<long, TripSegmentStats?> results = new Dictionary<long, TripSegmentStats?>(spans.Count);
        List<TripSpan> ordered = spans.OrderBy(span => span.Start).ToList();
        int fixesRead = 0;
        int next = 0;

        while (next < ordered.Count)
        {
            int room = Math.Min(_windowFixes, maxFixes - fixesRead);
            if (room <= 0)
            {
                break;
            }

            TripSpan first = ordered[next];
            List<TripPoint> fixes = await _fixes.ReadFromAsync(deviceRowId, first.Start, room + 1, cancellationToken);
            bool truncated = fixes.Count > room;
            if (truncated)
            {
                fixes.RemoveAt(fixes.Count - 1);
            }

            fixesRead += fixes.Count;
            TripPoint? beforeWindow = await _fixes.ReadBeforeAsync(deviceRowId, first.Start, cancellationToken);

            int rebuiltInWindow = 0;
            while (next < ordered.Count)
            {
                TripSpan span = ordered[next];
                int startIndex = LowerBound(fixes, span.Start);
                if (startIndex >= fixes.Count)
                {
                    break;
                }

                // The start fix is gone (erased since detection); the worker is already
                // marked to recompute this stretch.
                if (fixes[startIndex].FixTime != span.Start)
                {
                    results[span.Id] = null;
                    next++;
                    rebuiltInWindow++;
                    continue;
                }

                TripSegmentStats? stats = AnalyzeInWindow(fixes, truncated, startIndex, beforeWindow, span, settings, nowUtc);
                if (stats is null)
                {
                    break;
                }

                results[span.Id] = stats;
                next++;
                rebuiltInWindow++;
            }

            if (rebuiltInWindow == 0)
            {
                // One trip needs more than a whole window: rebuild it on its own.
                results[first.Id] = await _fixes.AnalyzeAsync(
                    deviceRowId,
                    first.Start,
                    first.NextStart,
                    settings,
                    nowUtc,
                    Math.Max(maxFixes - fixesRead, 0),
                    cancellationToken);
                next++;
            }
        }

        // Out of budget: whatever is left is pending.
        for (; next < ordered.Count; next++)
        {
            results[ordered[next].Id] = null;
        }

        return results;
    }

    // Rebuilds one trip from fixes already in memory, or returns null when the window
    // ends before the trip's end could be decided.
    private static TripSegmentStats? AnalyzeInWindow(
        List<TripPoint> fixes,
        bool truncated,
        int startIndex,
        TripPoint? beforeWindow,
        TripSpan span,
        TripDetectionSettings settings,
        DateTime nowUtc)
    {
        int endIndex = fixes.Count - 1;
        bool reachesNext = false;
        if (span.NextStart is DateTime nextStart)
        {
            int nextIndex = LowerBound(fixes, nextStart);
            if (nextIndex < fixes.Count && fixes[nextIndex].FixTime == nextStart)
            {
                endIndex = nextIndex;
                reachesNext = true;
            }
            else if (nextIndex < fixes.Count)
            {
                // The successor's start fix is gone; stop just before where it was.
                endIndex = Math.Max(startIndex, nextIndex - 1);
            }
        }

        List<TripPoint> slice = fixes.GetRange(startIndex, endIndex - startIndex + 1);
        bool moreDataAvailable = truncated && !reachesNext && endIndex == fixes.Count - 1;
        TripPoint? previous = startIndex > 0 ? fixes[startIndex - 1] : beforeWindow;

        return TripSegmentAnalyzer.Analyze(
            previous,
            slice,
            settings,
            span.NextStart is null ? nowUtc : null,
            moreDataAvailable,
            reachesNext);
    }

    // First index whose fix time is at or after the given time (fixes are sorted).
    private static int LowerBound(List<TripPoint> fixes, DateTime time)
    {
        int low = 0;
        int high = fixes.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (fixes[middle].FixTime < time)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
