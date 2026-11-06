using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the one authorisation rule that is pure logic rather than a database
/// query: sharing implies settings.
///
/// It is worth pinning down here because every entry point that sets a grant's
/// capabilities — creating an invitation, and editing a grant — funnels through
/// <see cref="CapabilitySet.FromRequest"/>. If the
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

    [Fact]
    public void AGainIsMeasuredAgainstTheGranter()
    {
        CapabilitySet granter = new CapabilitySet(CanDelete: false, CanShare: true, CanModifySettings: true);

        Assert.True(CapabilitySet.Full().GainsBeyond(granter, CapabilitySet.None()));
        Assert.False(new CapabilitySet(false, true, true).GainsBeyond(granter, CapabilitySet.None()));
    }

    [Fact]
    public void KeepingOrDroppingAnExistingCapabilityIsNotAGain()
    {
        CapabilitySet granter = CapabilitySet.None();
        CapabilitySet current = CapabilitySet.Full();

        Assert.False(CapabilitySet.Full().GainsBeyond(granter, current));
        Assert.False(CapabilitySet.None().GainsBeyond(granter, current));
    }

    [Fact]
    public void EachCapabilityIsCheckedOnItsOwn()
    {
        // One test per flag, so a refactor that drops one of the three clauses fails
        // here instead of in production.
        CapabilitySet nothing = CapabilitySet.None();

        Assert.True(new CapabilitySet(true, false, false).GainsBeyond(nothing, nothing));
        Assert.True(new CapabilitySet(false, true, false).GainsBeyond(nothing, nothing));
        Assert.True(new CapabilitySet(false, false, true).GainsBeyond(nothing, nothing));
    }

    [Fact]
    public void OfReadsTheThreeFlagsOfAGrant()
    {
        CapabilitySet capabilities = CapabilitySet.Of(new DevicePermissionsDto(true, true, false, true));

        Assert.Equal(new CapabilitySet(true, false, true), capabilities);
    }
}
