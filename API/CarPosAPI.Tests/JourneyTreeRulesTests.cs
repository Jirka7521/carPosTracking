using CarPosAPI.Dtos;
using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="JourneyTreeRules"/> — nesting depth, cycles and date ranges. The
/// range rule (disjoint or nested, never partly overlapping or identical) is what makes
/// "the innermost journey whose range holds the trip" a single answer.
/// </summary>
public sealed class JourneyTreeRulesTests
{
    private static readonly DateTime July1 = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Nesting_UpToTheMaximumDepth_IsAllowed_OneMoreIsNot()
    {
        List<JourneySnapshot> chain = Chain(TripRules.MaxJourneyDepth);

        Assert.Null(JourneyTreeRules.CheckPlacement(null, chain[^2].Id, chain));
        Assert.Equal(JourneyPlacementProblem.TooDeep, JourneyTreeRules.CheckPlacement(null, chain[^1].Id, chain));
    }

    [Fact]
    public void MovingASubtree_CountsItsOwnDepth()
    {
        // A two-level subtree may go under a level-3 journey (3 + 2 = 5) but not under
        // a level-4 one.
        List<JourneySnapshot> chain = Chain(4);
        JourneySnapshot moved = Journey("Moved", null);
        JourneySnapshot child = Journey("Child", moved.Id);
        List<JourneySnapshot> all = chain.Concat(new[] { moved, child }).ToList();

        Assert.Null(JourneyTreeRules.CheckPlacement(moved.Id, chain[2].Id, all));
        Assert.Equal(JourneyPlacementProblem.TooDeep, JourneyTreeRules.CheckPlacement(moved.Id, chain[3].Id, all));
    }

    [Fact]
    public void MovingAJourneyIntoItselfOrItsDescendant_IsACycle()
    {
        List<JourneySnapshot> chain = Chain(3);

        Assert.Equal(JourneyPlacementProblem.Cycle, JourneyTreeRules.CheckPlacement(chain[0].Id, chain[0].Id, chain));
        Assert.Equal(JourneyPlacementProblem.Cycle, JourneyTreeRules.CheckPlacement(chain[0].Id, chain[2].Id, chain));
        Assert.Null(JourneyTreeRules.CheckPlacement(chain[2].Id, null, chain));
    }

    [Fact]
    public void AParentFromElsewhere_IsNoSuchJourney()
    {
        Assert.Equal(
            JourneyPlacementProblem.NoSuchJourney,
            JourneyTreeRules.CheckPlacement(null, Guid.NewGuid(), Chain(2)));
    }

    [Fact]
    public void Ranges_MayBeDisjointOrNested_ButNotPartlyOverlappingOrIdentical()
    {
        JourneySnapshot summer = Ranged("Summer", July1.AddDays(-30), July1.AddDays(62));
        JourneySnapshot croatia = Ranged("Croatia", July1, July1.AddDays(14));
        List<JourneySnapshot> all = new List<JourneySnapshot> { summer, croatia };

        // Inside Croatia, after it, and wrapping Summer: all fine.
        Assert.Null(JourneyTreeRules.FindRangeConflict(null, July1.AddDays(2), July1.AddDays(4), all));
        Assert.Null(JourneyTreeRules.FindRangeConflict(null, July1.AddDays(14), July1.AddDays(20), all));
        Assert.Null(JourneyTreeRules.FindRangeConflict(null, July1.AddDays(-60), July1.AddDays(90), all));

        // Straddling Croatia's end: refused, naming Croatia.
        Assert.Equal(croatia, JourneyTreeRules.FindRangeConflict(null, July1.AddDays(10), July1.AddDays(20), all));

        // Exactly Croatia's dates: refused — "innermost" would be ambiguous.
        Assert.Equal(croatia, JourneyTreeRules.FindRangeConflict(null, July1, July1.AddDays(14), all));

        // A journey never conflicts with itself when its own range is saved again.
        Assert.Null(JourneyTreeRules.FindRangeConflict(croatia.Id, July1, July1.AddDays(14), all));
    }

    [Fact]
    public void Subtree_HoldsTheJourneyAndEverythingInsideIt()
    {
        List<JourneySnapshot> chain = Chain(4);
        JourneySnapshot sibling = Journey("Sibling", chain[0].Id);
        List<JourneySnapshot> all = chain.Append(sibling).ToList();

        HashSet<Guid> subtree = JourneyTreeRules.Subtree(chain[1].Id, all);

        Assert.Equal(new[] { chain[1].Id, chain[2].Id, chain[3].Id }.ToHashSet(), subtree);
    }

    // Level 1 → level n, each inside the previous.
    private static List<JourneySnapshot> Chain(int levels)
    {
        List<JourneySnapshot> chain = new List<JourneySnapshot>();
        Guid? parent = null;
        for (int level = 1; level <= levels; level++)
        {
            JourneySnapshot journey = Journey($"Level {level}", parent);
            chain.Add(journey);
            parent = journey.Id;
        }

        return chain;
    }

    private static JourneySnapshot Journey(string name, Guid? parentId) =>
        new JourneySnapshot(Guid.NewGuid(), parentId, name, null, null);

    private static JourneySnapshot Ranged(string name, DateTime from, DateTime to) =>
        new JourneySnapshot(Guid.NewGuid(), null, name, from, to);
}
