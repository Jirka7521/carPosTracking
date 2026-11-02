using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins the escalation rules on sharing grants. Before them, <c>CanShare</c> was a
/// master key: a sharer could tick <c>CanDelete</c> on their own grant, or hand it to
/// a second account of their own, though nobody had ever given it to them. Neither
/// shows up as an error anywhere — only as a permission that should not exist — so
/// each path is asserted here rather than assumed.
/// </summary>
public sealed class GrantChangePolicyTests
{
    private const int Caller = 7;
    private const int Other = 8;

    /// <summary>A sharer who may not delete: share implies settings, delete not held.</summary>
    private static readonly CapabilitySet SharerWithoutDelete = new CapabilitySet(false, true, true);

    [Fact]
    public void ASharerCannotEditTheirOwnGrant()
    {
        // The headline case: promoting oneself to CanDelete.
        ServiceError? refusal = GrantChangePolicy.CheckUpdate(
            Caller,
            SharerWithoutDelete,
            Caller,
            SharerWithoutDelete,
            CapabilitySet.Full());

        Assert.NotNull(refusal);
        Assert.Equal(ErrorCodes.CannotChangeOwnAccess, refusal.Code);
    }

    [Fact]
    public void EvenADemotionOfOnesOwnGrantIsRefused()
    {
        // "Never your own" rather than "never your own upwards": one rule with no
        // exceptions to get wrong. Leaving is a revoke, which stays allowed.
        ServiceError? refusal = GrantChangePolicy.CheckUpdate(
            Caller,
            CapabilitySet.Full(),
            Caller,
            CapabilitySet.Full(),
            new CapabilitySet(false, false, false));

        Assert.NotNull(refusal);
        Assert.Equal(ErrorCodes.CannotChangeOwnAccess, refusal.Code);
    }

    [Fact]
    public void ACapabilityTheCallerLacksCannotBeGranted()
    {
        ServiceError? onCreate = GrantChangePolicy.CheckCreate(
            SharerWithoutDelete,
            CapabilitySet.FromRequest(canDelete: true, canShare: false, canModifySettings: false));

        ServiceError? onUpdate = GrantChangePolicy.CheckUpdate(
            Caller,
            SharerWithoutDelete,
            Other,
            CapabilitySet.None(),
            CapabilitySet.FromRequest(canDelete: true, canShare: false, canModifySettings: false));

        Assert.NotNull(onCreate);
        Assert.Equal(ErrorCodes.CannotGrantUnheldCapability, onCreate.Code);
        Assert.NotNull(onUpdate);
        Assert.Equal(ErrorCodes.CannotGrantUnheldCapability, onUpdate.Code);
    }

    [Fact]
    public void WhatTheCallerHoldsCanBeGranted()
    {
        Assert.Null(GrantChangePolicy.CheckCreate(
            SharerWithoutDelete,
            CapabilitySet.FromRequest(canDelete: false, canShare: true, canModifySettings: false)));

        Assert.Null(GrantChangePolicy.CheckCreate(CapabilitySet.Full(), CapabilitySet.Full()));
    }

    [Fact]
    public void ACapabilityTheGranteeAlreadyHasMayBeKeptOrRemoved()
    {
        // PUT is a full replacement, so a sharer without CanDelete editing somebody
        // who has it must be able to echo it back unchanged — that is not a grant.
        CapabilitySet current = new CapabilitySet(true, false, false);

        Assert.Null(GrantChangePolicy.CheckUpdate(
            Caller,
            SharerWithoutDelete,
            Other,
            current,
            new CapabilitySet(true, false, true)));

        // Taking it away only ever reduces what exists.
        Assert.Null(GrantChangePolicy.CheckUpdate(
            Caller,
            SharerWithoutDelete,
            Other,
            current,
            CapabilitySet.None()));
    }

    [Fact]
    public void ARevokedGrantIsNotARightToItsOldCapabilities()
    {
        // Re-granting reactivates the revoked row, but its old flags are history:
        // CheckCreate measures against nothing, so CanDelete cannot ride back in on
        // a reactivation by a sharer who does not hold it.
        Assert.NotNull(GrantChangePolicy.CheckCreate(SharerWithoutDelete, CapabilitySet.Full()));
    }
}
