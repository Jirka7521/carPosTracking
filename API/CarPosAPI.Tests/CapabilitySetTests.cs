using CarPosAPI.Services.Authorization;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the one authorisation rule that is pure logic rather than a database
/// query: sharing implies settings.
///
/// It is worth pinning down here because both entry points that create grants —
/// registering a device with <c>additionalAccesses</c>, and the sharing
/// endpoints — funnel through <see cref="CapabilitySet.FromRequest"/>. If the
/// coercion were ever dropped, the result would not be a crash but a quietly
/// wrong permission set: a user who can hand out rights they do not hold.
/// </summary>
public sealed class CapabilitySetTests
{
    [Fact]
    public void SharingImpliesSettings()
    {
        // The combination a client is most likely to send: "let them share it",
        // with the settings box left untouched.
        CapabilitySet capabilities = CapabilitySet.FromRequest(canDelete: false, canShare: true, canModifySettings: false);

        Assert.True(capabilities.CanShare);
        Assert.True(capabilities.CanModifySettings);
    }

    [Fact]
    public void LeavesOtherCombinationsAlone()
    {
        CapabilitySet capabilities = CapabilitySet.FromRequest(canDelete: true, canShare: false, canModifySettings: false);

        Assert.True(capabilities.CanDelete);
        Assert.False(capabilities.CanShare);
        // No coercion happens without CanShare — settings must stay off.
        Assert.False(capabilities.CanModifySettings);
    }

    [Fact]
    public void SettingsWithoutSharingIsPermitted()
    {
        // The reverse implication does not hold: managing a device's settings says
        // nothing about being allowed to give it away.
        CapabilitySet capabilities = CapabilitySet.FromRequest(canDelete: false, canShare: false, canModifySettings: true);

        Assert.False(capabilities.CanShare);
        Assert.True(capabilities.CanModifySettings);
    }

    [Fact]
    public void TheCreatorGetsEverything()
    {
        CapabilitySet capabilities = CapabilitySet.Full();

        Assert.True(capabilities.CanDelete);
        Assert.True(capabilities.CanShare);
        Assert.True(capabilities.CanModifySettings);
    }
}
