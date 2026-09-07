using CarPosAPI.Options;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the startup gate that stops a deployment publishing a privacy policy
/// nobody can write to.
///
/// A policy naming no reachable controller is worse than no policy: it looks like
/// an answer to a data-subject request while making one impossible. The check is
/// deliberately dumb — it cannot know whether a mailbox is read — so the only thing
/// worth pinning down is that it refuses the placeholder the repository ships with,
/// which is the exact value a real deployment forgets to change.
/// </summary>
public sealed class PrivacyOptionsTests
{
    [Fact]
    public void RejectsTheShippedPlaceholder()
    {
        PrivacyOptions options = new PrivacyOptions();

        // Straight out of appsettings.json, untouched.
        Assert.Equal(PrivacyOptions.UnsetContactPlaceholder, options.ControllerContactEmail);
        Assert.False(options.HasController());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public void RejectsAnUnusableContact(string contact)
    {
        PrivacyOptions options = new PrivacyOptions { ControllerContactEmail = contact };

        Assert.False(options.HasController());
    }

    [Fact]
    public void RejectsThePlaceholderWhateverItsCase()
    {
        // Someone "filling it in" by retyping it differently should not get past.
        PrivacyOptions options = new PrivacyOptions
        {
            ControllerContactEmail = PrivacyOptions.UnsetContactPlaceholder.ToLowerInvariant(),
        };

        Assert.False(options.HasController());
    }

    [Fact]
    public void AcceptsARealAddress()
    {
        PrivacyOptions options = new PrivacyOptions { ControllerContactEmail = "privacy@example.org" };

        Assert.True(options.HasController());
    }
}
