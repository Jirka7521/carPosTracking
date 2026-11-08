using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// The rules that span journeys — nesting depth, cycles, subtrees and date ranges — as
/// pure functions over <see cref="JourneySnapshot"/>s, so they are tested without a
/// database and <c>JourneyService</c> only has to load the device's journeys and ask.
///
/// <para>
/// <b>Ranges are disjoint or nested.</b> Two journeys' ranges may not partly overlap, and
/// may not be identical. That is what makes "the innermost journey whose range holds the
/// trip" (<see cref="TripQueries"/>) always a single, predictable answer — "Summer 2026"
/// may contain "Croatia", but "1–10 July" and "5–14 July" cannot both exist.
/// </para>
///
/// <para>
/// Every walk is bounded by the number of journeys, so even corrupt data (a cycle that
/// somehow reached the table) cannot spin forever.
/// </para>
/// </summary>
internal static class JourneyTreeRules
{
    /// <summary>The level a journey would sit at under <paramref name="parentId"/> (top level is 1).</summary>
    /// <param name="parentId">The intended parent, or null.</param>
    /// <param name="byId">All of the device's journeys, by id.</param>
    /// <returns>The level.</returns>
    public static int LevelUnder(Guid? parentId, IReadOnlyDictionary<Guid, JourneySnapshot> byId)
    {
        int level = 1;
        Guid? current = parentId;
        while (current is Guid id && byId.TryGetValue(id, out JourneySnapshot? node) && level <= byId.Count)
        {
            level++;
            current = node.ParentId;
        }

        return level;
    }

    /// <summary>How many levels the subtree rooted at <paramref name="rootId"/> spans (a leaf is 1).</summary>
    /// <param name="rootId">The subtree's root.</param>
    /// <param name="all">All of the device's journeys.</param>
    /// <returns>The subtree's height.</returns>
    public static int SubtreeHeight(Guid rootId, IReadOnlyCollection<JourneySnapshot> all)
    {
        ILookup<Guid?, Guid> children = all.ToLookup(journey => journey.ParentId, journey => journey.Id);
        int height = 0;
        List<Guid> level = new List<Guid> { rootId };
        HashSet<Guid> seen = new HashSet<Guid> { rootId };

        while (level.Count > 0)
        {
            height++;
            List<Guid> nextLevel = new List<Guid>();
            foreach (Guid id in level)
            {
                foreach (Guid child in children[id])
                {
                    if (seen.Add(child))
                    {
                        nextLevel.Add(child);
                    }
                }
            }

            level = nextLevel;
        }

        return height;
    }

    /// <summary>The journey and every journey inside it.</summary>
    /// <param name="rootId">The subtree's root.</param>
    /// <param name="all">All of the device's journeys.</param>
    /// <returns>The ids, including <paramref name="rootId"/>.</returns>
    public static HashSet<Guid> Subtree(Guid rootId, IReadOnlyCollection<JourneySnapshot> all)
    {
        ILookup<Guid?, Guid> children = all.ToLookup(journey => journey.ParentId, journey => journey.Id);
        HashSet<Guid> subtree = new HashSet<Guid> { rootId };
        Queue<Guid> pending = new Queue<Guid>();
        pending.Enqueue(rootId);

        while (pending.Count > 0)
        {
            foreach (Guid child in children[pending.Dequeue()])
            {
                if (subtree.Add(child))
                {
                    pending.Enqueue(child);
                }
            }
        }

        return subtree;
    }

    /// <summary>
    /// The first problem with placing <paramref name="journeyId"/> (null for a new journey)
    /// under <paramref name="parentId"/>, or null when the placement is fine.
    /// </summary>
    /// <param name="journeyId">The journey being moved, or null when creating one.</param>
    /// <param name="parentId">The intended parent, or null for the top level.</param>
    /// <param name="all">All of the device's journeys.</param>
    /// <returns>The error, or null.</returns>
    public static JourneyPlacementProblem? CheckPlacement(Guid? journeyId, Guid? parentId, IReadOnlyCollection<JourneySnapshot> all)
    {
        Dictionary<Guid, JourneySnapshot> byId = all.ToDictionary(journey => journey.Id);

        if (parentId is Guid parent)
        {
            if (!byId.ContainsKey(parent))
            {
                return JourneyPlacementProblem.NoSuchJourney;
            }

            if (journeyId is Guid self && Subtree(self, all).Contains(parent))
            {
                return JourneyPlacementProblem.Cycle;
            }
        }

        int height = journeyId is Guid moved ? SubtreeHeight(moved, all) : 1;
        return LevelUnder(parentId, byId) + height - 1 > TripRules.MaxJourneyDepth
            ? JourneyPlacementProblem.TooDeep
            : null;
    }

    /// <summary>
    /// The journey whose range conflicts with <c>[from, to)</c> — partly overlapping it,
    /// or identical to it — or null when the range is acceptable.
    /// </summary>
    /// <param name="journeyId">The journey being saved (excluded), or null when creating one.</param>
    /// <param name="from">Inclusive start.</param>
    /// <param name="to">Exclusive end.</param>
    /// <param name="all">All of the device's journeys.</param>
    /// <returns>The conflicting journey, or null.</returns>
    public static JourneySnapshot? FindRangeConflict(
        Guid? journeyId,
        DateTime from,
        DateTime to,
        IReadOnlyCollection<JourneySnapshot> all)
    {
        foreach (JourneySnapshot other in all)
        {
            if (other.Id == journeyId || other.RangeFrom is not DateTime otherFrom || other.RangeTo is not DateTime otherTo)
            {
                continue;
            }

            bool identical = from == otherFrom && to == otherTo;
            bool disjoint = to <= otherFrom || otherTo <= from;
            bool insideOther = otherFrom <= from && to <= otherTo;
            bool containsOther = from <= otherFrom && otherTo <= to;

            if (identical || !(disjoint || insideOther || containsOther))
            {
                return other;
            }
        }

        return null;
    }
}
