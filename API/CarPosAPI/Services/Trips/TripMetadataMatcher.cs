namespace CarPosAPI.Services.Trips;

/// <summary>
/// Carries the user's data across a recompute: when late fixes or new thresholds move,
/// split or merge trips, decides which existing row each new start takes over.
///
/// <para>
/// <b>Why it matters.</b> Replacing rows wholesale would give every recomputed trip a new
/// id — breaking an open detail page — and throw away names and manual placements.
/// Instead each new start is paired 1:1 with an old trip and that row is updated in
/// place, keeping its id, name and journey.
/// </para>
///
/// <para>
/// <b>Spans.</b> A trip's span is <c>[start, next start)</c>: the trip plus the stop after
/// it, the stretch of history it "owns". Spans use starts alone — the newest trip's end is
/// still open, and a stored end simply moves with its start — so the caller carries each
/// start's end across by itself.
/// The last span of each list ends at a <b>common horizon</b> just past the latest start
/// of either list. With open-ended last spans a newly arrived trip would overlap the
/// previous latest trip by more than that trip's own successor does, and steal its row.
/// </para>
///
/// <para>
/// <b>Who wins.</b> Candidates are taken greedily in this order: a trip the user touched
/// (pinned or named) first, so a merge keeps the user's data; then an unchanged start,
/// so a split leaves the first piece on its row; then the larger overlap. A piece split
/// off a trip gets a new row with no manual placement; it follows the journeys' date
/// ranges like any other trip.
/// </para>
/// </summary>
internal static class TripMetadataMatcher
{
    /// <summary>Pairs detected starts with existing rows.</summary>
    /// <param name="existing">The rows being replaced (any order).</param>
    /// <param name="detectedStarts">The newly detected starts for the same stretch (any order).</param>
    /// <returns>What to update, insert and delete.</returns>
    public static TripMatchResult Match(IReadOnlyList<StoredTripSnapshot> existing, IReadOnlyList<DateTime> detectedStarts)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(detectedStarts);

        StoredTripSnapshot[] olds = existing
            .OrderBy(trip => trip.StartTime)
            .ThenBy(trip => trip.Id)
            .ToArray();
        DateTime[] news = detectedStarts.Distinct().OrderBy(start => start).ToArray();

        long horizon = 1 + Math.Max(
            olds.Length > 0 ? olds[^1].StartTime.Ticks : 0,
            news.Length > 0 ? news[^1].Ticks : 0);

        List<OverlapCandidate> candidates = Overlaps(olds, news, horizon);

        candidates.Sort((left, right) =>
        {
            int byUserData = right.Old.HasUserData.CompareTo(left.Old.HasUserData);
            if (byUserData != 0)
            {
                return byUserData;
            }

            bool leftSame = left.Old.StartTime == news[left.NewIndex];
            bool rightSame = right.Old.StartTime == news[right.NewIndex];
            int bySameStart = rightSame.CompareTo(leftSame);
            if (bySameStart != 0)
            {
                return bySameStart;
            }

            int byOverlap = right.OverlapTicks.CompareTo(left.OverlapTicks);
            if (byOverlap != 0)
            {
                return byOverlap;
            }

            int byOldStart = left.Old.StartTime.CompareTo(right.Old.StartTime);
            return byOldStart != 0 ? byOldStart : left.NewIndex.CompareTo(right.NewIndex);
        });

        StoredTripSnapshot?[] takenBy = new StoredTripSnapshot?[news.Length];
        HashSet<long> usedOldIds = new HashSet<long>();
        foreach (OverlapCandidate candidate in candidates)
        {
            if (takenBy[candidate.NewIndex] is null && !usedOldIds.Contains(candidate.Old.Id))
            {
                takenBy[candidate.NewIndex] = candidate.Old;
                usedOldIds.Add(candidate.Old.Id);
            }
        }

        List<TripMatch> matches = new List<TripMatch>(news.Length);
        for (int newIndex = 0; newIndex < news.Length; newIndex++)
        {
            StoredTripSnapshot? owner = takenBy[newIndex];
            matches.Add(owner is null
                ? new TripMatch(news[newIndex], null, null, false, null)
                : new TripMatch(news[newIndex], owner.Id, owner.JourneyId, owner.JourneyPinned, owner.Name));
        }

        List<long> removed = olds
            .Where(trip => !usedOldIds.Contains(trip.Id))
            .Select(trip => trip.Id)
            .ToList();

        return new TripMatchResult(matches, removed);
    }

    // Every (old, new) pair whose spans overlap, found with a single merge-style sweep —
    // both lists are sorted and their spans tile time, so the work is linear.
    private static List<OverlapCandidate> Overlaps(StoredTripSnapshot[] olds, DateTime[] news, long horizon)
    {
        List<OverlapCandidate> candidates = new List<OverlapCandidate>();
        int oldIndex = 0;
        int newIndex = 0;

        while (oldIndex < olds.Length && newIndex < news.Length)
        {
            long oldStart = olds[oldIndex].StartTime.Ticks;
            long oldEnd = oldIndex + 1 < olds.Length ? olds[oldIndex + 1].StartTime.Ticks : horizon;
            long newStart = news[newIndex].Ticks;
            long newEnd = newIndex + 1 < news.Length ? news[newIndex + 1].Ticks : horizon;

            long overlap = Math.Min(oldEnd, newEnd) - Math.Max(oldStart, newStart);
            if (overlap > 0)
            {
                candidates.Add(new OverlapCandidate(olds[oldIndex], newIndex, overlap));
            }

            // Advance whichever span ends first; on a tie, both.
            if (oldEnd < newEnd)
            {
                oldIndex++;
            }
            else if (newEnd < oldEnd)
            {
                newIndex++;
            }
            else
            {
                oldIndex++;
                newIndex++;
            }
        }

        return candidates;
    }
}
