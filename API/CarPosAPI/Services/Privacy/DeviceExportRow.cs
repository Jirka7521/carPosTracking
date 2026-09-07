namespace CarPosAPI.Services.Privacy;

/// <summary>
/// A readable device's metadata as it appears in a data export.
///
/// Note what is not here: <c>PrivateKeyCiphertext</c>. The sealed device key is not
/// the user's personal data, and copying it out of the system would weaken the one
/// thing that keeps stored envelopes shut.
/// </summary>
/// <param name="RowId">Internal id, needed to stream the device's positions.</param>
/// <param name="DeviceId">The device's MQTT identity.</param>
/// <param name="DisplayName">The shared display name, if set.</param>
/// <param name="IsActive">False once soft-deleted.</param>
/// <param name="CreatedAt">When the device was registered (UTC).</param>
/// <param name="LastSeenAt">When it last reported (UTC), if ever.</param>
public sealed record DeviceExportRow(
    Guid RowId,
    string DeviceId,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastSeenAt);
