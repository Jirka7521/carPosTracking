using CarPosAPI.Dtos;
using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// How a share link is described to its creator.
///
/// The precedence between states is the part worth testing: a link can be several
/// things at once — revoked <em>and</em> expired, cooling down <em>and</em> past
/// its window — and the creator needs the most informative of those, not the first
/// one an <c>if</c> happened to reach.
/// </summary>
public sealed class ShareLinkStatusResolverTests
{
    private static readonly DateTime Now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowStart = Now.AddHours(-1);
    private static readonly DateTime WindowEnd = Now.AddHours(1);

    [Fact]
    public void ALiveLinkIsActive()
    {
        Assert.Equal(
            ShareLinkStatusNames.Active,
            ShareLinkStatusResolver.Resolve(null, WindowStart, WindowEnd, null, Now));
    }

    [Fact]
    public void ALinkBeforeItsWindowIsScheduled()
    {
        Assert.Equal(
            ShareLinkStatusNames.Scheduled,
            ShareLinkStatusResolver.Resolve(null, Now.AddHours(1), Now.AddHours(2), null, Now));
    }

    [Fact]
    public void ALinkPastItsWindowIsExpired()
    {
        Assert.Equal(
            ShareLinkStatusNames.Expired,
            ShareLinkStatusResolver.Resolve(null, Now.AddHours(-3), Now.AddHours(-2), null, Now));
    }

    [Fact]
    public void ALockedLinkInsideItsWindowIsCoolingDown()
    {
        Assert.Equal(
            ShareLinkStatusNames.CoolingDown,
            ShareLinkStatusResolver.Resolve(null, WindowStart, WindowEnd, Now.AddMinutes(5), Now));
    }

    [Fact]
    public void AnElapsedCooldownIsNotReported()
    {
        // The lock is over; saying so would be noise in a list the creator scans for
        // the one link that needs attention.
        Assert.Equal(
            ShareLinkStatusNames.Active,
            ShareLinkStatusResolver.Resolve(null, WindowStart, WindowEnd, Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void RevocationOutranksExpiry()
    {
        // Both are true. "Revoked" is the more informative fact, because somebody did
        // it deliberately and may need to remember that they did.
        Assert.Equal(
            ShareLinkStatusNames.Revoked,
            ShareLinkStatusResolver.Resolve(Now.AddHours(-4), Now.AddHours(-3), Now.AddHours(-2), null, Now));
    }

    [Fact]
    public void ARevokedLinkCanNeverBeEditedOrReissued()
    {
        // The one irreversible act in the feature, and this predicate gates BOTH
        // ways back: editing its window and minting it fresh secrets. Somebody
        // revokes a link when it has reached the wrong person; either path
        // reopening it would undo that silently.
        Assert.False(ShareLinkStatusResolver.IsEditable(Now.AddMinutes(-1)));
        Assert.False(ShareLinkStatusResolver.IsEditable(Now.AddYears(-5)));
    }

    [Fact]
    public void AnExpiredLinkStaysEditableSoItsWindowCanBeExtended()
    {
        // Expiry is time passing rather than a decision, and the recipient already
        // holds the link — so extending is the same act as sending a fresh one,
        // minus having to communicate a new code.
        Assert.True(ShareLinkStatusResolver.IsEditable(null));

        Assert.Equal(
            ShareLinkStatusNames.Expired,
            ShareLinkStatusResolver.Resolve(null, Now.AddHours(-3), Now.AddHours(-2), null, Now));
    }

    [Fact]
    public void ExpiryOutranksACooldownThatNoLongerMatters()
    {
        // A link nobody can reach is not "cooling down" in any sense the creator
        // cares about; reporting the lock would bury the reason it stopped working.
        Assert.Equal(
            ShareLinkStatusNames.Expired,
            ShareLinkStatusResolver.Resolve(null, Now.AddHours(-3), Now.AddHours(-2), Now.AddHours(5), Now));
    }
}
