namespace CarPosAPI.Services.Privacy;

/// <summary>
/// One open invitation the account made, as it appears in a data export.
///
/// <para>
/// <b>No field for the link:</b> neither its public selector nor the verifier's
/// digest. The same defence as <see cref="ShareLinkExportRow"/> — a credential with no
/// property to travel in cannot be exported however this document is later reshaped,
/// and <c>DataExportShapeTests</c> fails the build if one appears. There is no
/// recipient to export either: an invitation is addressed to nobody until someone
/// accepts it, and then it is gone and the grant it became is exported instead.
/// </para>
/// </summary>
/// <param name="DeviceId">MQTT identity of the device the invitation is for.</param>
/// <param name="CanDelete">Capability on offer.</param>
/// <param name="CanShare">Capability on offer.</param>
/// <param name="CanModifySettings">Capability on offer.</param>
/// <param name="CreatedAt">When the invitation was made (UTC).</param>
/// <param name="ExpiresAt">When it stops being acceptable (UTC).</param>
public sealed record AccessInvitationExportRow(
    string DeviceId,
    bool CanDelete,
    bool CanShare,
    bool CanModifySettings,
    DateTime CreatedAt,
    DateTime ExpiresAt);
