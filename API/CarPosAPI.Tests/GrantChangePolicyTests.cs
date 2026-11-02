using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins the escalation rules on sharing grants. Before them, <c>CanShare</c> was a
/// master key: a sharer could tick <c>CanDelete</c> on their own grant, hand it to a
/// second account of their own, or remove the person who registered the device.
/// None of that shows up as an error anywhere — only as a permission that should
/// not exist — so each path is asserted here rather than assumed.
/// </summary>
public sealed class GrantChangePolicyTests
{
    /// <summary>A co-sharer who may not delete: share implies settings, delete not held.</summary>
    private static readonly GrantParty Sharer = new GrantParty(7, new CapabilitySet(false, true, true), IsOwner: false);

    /// <summary>The account that registered the device.</summary>
    private static readonly GrantParty Owner = new GrantParty(1, CapabilitySet.Full(), IsOwner: true);

    /// <summary>A co-sharer who was given the delete right by the owner.</summary>
    private static readonly GrantParty SharerWithDelete = new GrantParty(9, CapabilitySet.Full(), IsOwner: false);

    /// <summary>A read-only viewer.</summary>
    private static readonly GrantParty Viewer = new GrantParty(8, CapabilitySet.None(), IsOwner: false);

    [Fact]
    public void ASharerCannotEditTheirOwnGrant()
    {
        // The headline case: promoting oneself to CanDelete.
        ServiceError? refusal = GrantChangePolicy.CheckUpdate(Sharer, Sharer, CapabilitySet.Full());

        Assert.Equal(ErrorCodes.CannotChangeOwnAccess, refusal?.Code);
    }

    [Fact]
    public void EvenADemotionOfOnesOwnGrantIsRefused()
    {
        ServiceError? refusal = GrantChangePolicy.CheckUpdate(Owner, Owner, CapabilitySet.None());

        Assert.Equal(ErrorCodes.CannotChangeOwnAccess, refusal?.Code);
    }

    [Fact]
    public void TheOwnersGrantCannotBeEditedOrRevoked()
    {
        Assert.Equal(
            ErrorCodes.OwnerAccessImmutable,
            GrantChangePolicy.CheckUpdate(SharerWithDelete, Owner, CapabilitySet.None())?.Code);

        Assert.Equal(ErrorCodes.OwnerAccessImmutable, GrantChangePolicy.CheckRevoke(Owner)?.Code);
        Assert.Null(GrantChangePolicy.CheckRevoke(Sharer));
    }

    [Fact]
    public void OnlyTheOwnerGivesTheDeleteRight()
    {
        CapabilitySet withDelete = CapabilitySet.FromRequest(canDelete: true, canShare: false, canModifySettings: false);

        // Not even a co-sharer who holds CanDelete themselves.
        Assert.Equal(ErrorCodes.OnlyOwnerManagesDelete, GrantChangePolicy.CheckCreate(SharerWithDelete, withDelete)?.Code);
        Assert.Equal(ErrorCodes.OnlyOwnerManagesDelete, GrantChangePolicy.CheckUpdate(SharerWithDelete, Viewer, withDelete)?.Code);

        Assert.Null(GrantChangePolicy.CheckCreate(Owner, withDelete));
        Assert.Null(GrantChangePolicy.CheckUpdate(Owner, Viewer, withDelete));
    }

    [Fact]
    public void OnlyTheOwnerTakesTheDeleteRightAway()
    {
        Assert.Equal(
            ErrorCodes.OnlyOwnerManagesDelete,
            GrantChangePolicy.CheckUpdate(Sharer, SharerWithDelete, new CapabilitySet(false, true, true))?.Code);

        Assert.Null(GrantChangePolicy.CheckUpdate(Owner, SharerWithDelete, new CapabilitySet(false, true, true)));
    }

    [Fact]
    public void ACapabilityTheCallerLacksCannotBeGranted()
    {
        GrantParty settingsOnly = new GrantParty(10, new CapabilitySet(false, false, true), IsOwner: false);

        Assert.Equal(
            ErrorCodes.CannotGrantUnheldCapability,
            GrantChangePolicy.CheckCreate(settingsOnly, CapabilitySet.FromRequest(false, true, false))?.Code);
        Assert.Equal(
            ErrorCodes.CannotGrantUnheldCapability,
            GrantChangePolicy.CheckUpdate(settingsOnly, Viewer, CapabilitySet.FromRequest(false, true, false))?.Code);
    }

    [Fact]
    public void WhatTheCallerHoldsCanBeGranted()
    {
        Assert.Null(GrantChangePolicy.CheckCreate(Sharer, CapabilitySet.FromRequest(false, true, false)));
        Assert.Null(GrantChangePolicy.CheckCreate(Owner, CapabilitySet.Full()));
    }

    [Fact]
    public void ACapabilityTheGranteeAlreadyHasMayBeKept()
    {
        // PUT is a full replacement, so a sharer editing somebody who holds
        // CanDelete must be able to echo it back unchanged — that is not a grant.
        GrantParty grantee = new GrantParty(11, new CapabilitySet(true, false, false), IsOwner: false);

        Assert.Null(GrantChangePolicy.CheckUpdate(Sharer, grantee, new CapabilitySet(true, false, true)));
    }

    [Fact]
    public void ARevokedGrantIsNotARightToItsOldCapabilities()
    {
        // Re-granting reactivates the revoked row, but CheckCreate measures against
        // nothing, so a capability cannot ride back in on a reactivation.
        Assert.NotNull(GrantChangePolicy.CheckCreate(Sharer, CapabilitySet.Full()));
    }
}
