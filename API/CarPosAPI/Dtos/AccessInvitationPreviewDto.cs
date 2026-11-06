namespace CarPosAPI.Dtos;

/// <summary>
/// What the holder of an invitation link is shown before deciding: which device, who
/// is offering it, and how much it would let them do. Answered only for a link that
/// is still acceptable — an unknown, used, withdrawn or expired one gets the one
/// generic <c>invitationInvalid</c> error instead.
///
/// The inviter's name is disclosed to the link holder deliberately: the sharer chose
/// to hand them the link, and nobody should accept access without knowing whose it is.
/// Nothing about the link holder flows the other way until they accept.
/// </summary>
/// <param name="DeviceId">The device's MQTT identity.</param>
/// <param name="DeviceDisplayName">The device's display name, when it has one.</param>
/// <param name="InviterFirstName">Given name of the account offering access.</param>
/// <param name="InviterLastName">Family name of the account offering access.</param>
/// <param name="CanDelete">Capability the accepted grant will receive.</param>
/// <param name="CanShare">Capability the accepted grant will receive.</param>
/// <param name="CanModifySettings">Capability the accepted grant will receive.</param>
/// <param name="ExpiresAt">When the invitation stops being acceptable (UTC).</param>
/// <param name="AlreadyHasAccess">
/// True when the caller can already see the device, in which case accepting answers
/// 409 and leaves the invitation for whoever it was meant for.
/// </param>
public sealed record AccessInvitationPreviewDto(
    string DeviceId,
    string? DeviceDisplayName,
    string InviterFirstName,
    string InviterLastName,
    bool CanDelete,
    bool CanShare,
    bool CanModifySettings,
    DateTime ExpiresAt,
    bool AlreadyHasAccess);
