using CarPosAPI.Services.Authorization;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// One side of a grant change, as <see cref="GrantChangePolicy"/> needs to see it:
/// who, what they hold on the device, and whether theirs is the owner grant.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="Capabilities">What their active grant carries (<see cref="CapabilitySet.None"/> for nobody yet).</param>
/// <param name="IsOwner">Whether their grant is the device's owner grant.</param>
internal sealed record GrantParty(int UserId, CapabilitySet Capabilities, bool IsOwner);
