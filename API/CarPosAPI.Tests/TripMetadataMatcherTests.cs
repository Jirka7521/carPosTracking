using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="TripMetadataMatcher"/> — that a recompute keeps trip ids, names and
/// manual journey placements wherever the trip is still recognisably the same trip, and
/// that a newly arrived trip never takes over an existing one's row.
/// </summary>
public sealed class TripMetadataMatcherTests
{
    private static readonly DateTime Day = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Croatia = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void UnchangedStarts_KeepTheirRows()
    {
        StoredTripSnapshot[] existing =
        {
            Stored(1, 8, name: "Commute"),
            Stored(2, 12),
            Stored(3, 17, pinnedTo: Croatia),
        };

        TripMatchResult result = TripMetadataMatcher.Match(existing, new[] { At(8), At(12), At(17) });

        Assert.Equal(new long?[] { 1, 2, 3 }, result.Matches.Select(match => match.ExistingTripId));
        Assert.Equal("Commute", result.Matches[0].Name);
        Assert.Equal(Croatia, result.Matches[2].JourneyId);
        Assert.True(result.Matches[2].JourneyPinned);
        Assert.Empty(result.RemovedTripIds);
    }

    [Fact]
    public void NewTripAfterTheLatest_GetsANewRow_AndTheLatestKeepsItsOwn()
    {
        // The regression this class exists for: with open-ended spans the new trip would
        // overlap trip 2 by more than its own unchanged start does, and steal its row.
        StoredTripSnapshot[] existing = { Stored(1, 8), Stored(2, 12, pinnedTo: Croatia) };

        TripMatchResult result = TripMetadataMatcher.Match(existing, new[] { At(8), At(12), At(18) });

        Assert.Equal(new long?[] { 1, 2, null }, result.Matches.Select(match => match.ExistingTripId));
        Assert.Equal(Croatia, result.Matches[1].JourneyId);
        Assert.Null(result.Matches[2].JourneyId);
        Assert.False(result.Matches[2].JourneyPinned);
        Assert.Empty(result.RemovedTripIds);
    }

    [Fact]
    public void StartMovedByLateData_KeepsTheRow()
    {
        StoredTripSnapshot[] existing = { Stored(1, 8), Stored(2, 12, name: "Lunch") };

        TripMatchResult result = TripMetadataMatcher.Match(existing, new[] { At(8), At(11.9) });

        Assert.Equal(2, result.Matches[1].ExistingTripId);
        Assert.Equal("Lunch", result.Matches[1].Name);
    }

    [Fact]
    public void Merge_KeepsTheTripTheUserTouched_EvenWithLessOverlap()
    {
        StoredTripSnapshot[] existing =
        {
            Stored(1, 8),
            Stored(2, 9, pinnedTo: Croatia),
            Stored(3, 18),
        };

        // Trips 1 and 2 became one starting at 8:00.
        TripMatchResult result = TripMetadataMatcher.Match(existing, new[] { At(8), At(18) });

        Assert.Equal(2, result.Matches[0].ExistingTripId);
        Assert.Equal(Croatia, result.Matches[0].JourneyId);
        Assert.Equal(3, result.Matches[1].ExistingTripId);
        Assert.Equal(new long[] { 1 }, result.RemovedTripIds);
    }

    [Fact]
    public void Split_LeavesTheFirstPieceOnTheRow_AndTheNewPieceUnplaced()
    {
        StoredTripSnapshot[] existing = { Stored(1, 8, pinnedTo: Croatia, name: "Coast road"), Stored(2, 20) };

        // A stop discovered at 9:30 splits trip 1; the second piece is longer.
        TripMatchResult result = TripMetadataMatcher.Match(existing, new[] { At(8), At(9.5), At(20) });

        Assert.Equal(1, result.Matches[0].ExistingTripId);
        Assert.Equal("Coast road", result.Matches[0].Name);
        Assert.Null(result.Matches[1].ExistingTripId);
        Assert.False(result.Matches[1].JourneyPinned);
        Assert.Equal(2, result.Matches[2].ExistingTripId);
    }

    [Fact]
    public void TripsThatVanished_AreRemoved()
    {
        StoredTripSnapshot[] existing = { Stored(1, 8), Stored(2, 12), Stored(3, 17) };

        TripMatchResult result = TripMetadataMatcher.Match(existing, Array.Empty<DateTime>());

        Assert.Empty(result.Matches);
        Assert.Equal(new long[] { 1, 2, 3 }, result.RemovedTripIds);
    }

    [Fact]
    public void NoExistingTrips_AllNew()
    {
        TripMatchResult result = TripMetadataMatcher.Match(Array.Empty<StoredTripSnapshot>(), new[] { At(8), At(12) });

        Assert.All(result.Matches, match => Assert.Null(match.ExistingTripId));
        Assert.Empty(result.RemovedTripIds);
    }

    private static DateTime At(double hour) => Day.AddHours(hour);

    private static StoredTripSnapshot Stored(long id, double hour, Guid? pinnedTo = null, string? name = null) =>
        new StoredTripSnapshot(id, At(hour), pinnedTo, pinnedTo is not null, name);
}
