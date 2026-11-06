namespace CarPosAPI.Dtos;

/// <summary>
/// An open invitation as the device's sharers see it in the sharing list. Carries no
/// part of the link: the token is shown once, when the invitation is made, and never
/// again — it survives on the server only as a digest.
/// </summary>
/// <param name="Id">Identifies the invitation for withdrawing it.</param>
/// <param name="DeviceId">The device's MQTT identity.</param>
/// <param name="CreatedByUserId">The account that made it.</param>
/// <param name="CanDelete">Capability the accepted grant will receive.</param>
/// <param name="CanShare">Capability the accepted grant will receive.</param>
/// <param name="CanModifySettings">Capability the accepted grant will receive.</param>
/// <param name="CreatedAt">When it was made (UTC).</param>
/// <param name="ExpiresAt">When it stops being acceptable (UTC).</param>
public sealed record AccessInvitationDto(
    Guid Id,
    string DeviceId,
    int CreatedByUserId,
    bool CanDelete,
    bool CanShare,
    bool CanModifySettings,
    DateTime CreatedAt,
    DateTime ExpiresAt);
