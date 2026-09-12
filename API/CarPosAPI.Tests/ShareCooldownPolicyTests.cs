using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// The cooldown ladder, rung by rung.
///
/// It is clock-free by design — the caller passes "now" — which is the only reason
/// an hour-long lock can be asserted in a millisecond.
/// </summary>
public sealed class ShareCooldownPolicyTests
{
    private static readonly DateTime Now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheFirstFewMistakesCostNothing(int failedAttempts)
    {
        // Room to mistype a twelve-character code twice without being treated as an
        // attacker. A recipient reading it off a phone screen will use this.
        Assert.Null(ShareCooldownPolicy.NextUnlockUtc(failedAttempts, Now));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(6, 5)]
    [InlineData(7, 15)]
    [InlineData(8, 60)]
    public void TheLadderGrowsAsDocumented(int failedAttempts, int expectedMinutes)
    {
        DateTime? unlock = ShareCooldownPolicy.NextUnlockUtc(failedAttempts, Now);

        Assert.Equal(Now.AddMinutes(expectedMinutes), unlock);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(20)]
    [InlineData(5000)]
    public void TheLadderHoldsAtAnHourRatherThanRunningAway(int failedAttempts)
    {
        // Holding rather than growing is what keeps this a cooldown instead of the
        // permanent lock it was deliberately not made. It also means an index that
        // walks off the end of the ladder array is a case that cannot arise.
        Assert.Equal(Now.AddMinutes(60), ShareCooldownPolicy.NextUnlockUtc(failedAttempts, Now));
    }

    [Fact]
    public void AnHourlyCeilingStillMakesGuessingHopeless()
    {
        // Worth stating as an assertion rather than a comment: once saturated, the
        // ladder allows 24 attempts a day against a code of roughly 59 bits — and
        // only after the attacker already holds the link's 256-bit verifier.
        DateTime? unlock = ShareCooldownPolicy.NextUnlockUtc(100, Now);

        Assert.NotNull(unlock);
        Assert.True(unlock.Value - Now >= TimeSpan.FromMinutes(60));
    }
}
