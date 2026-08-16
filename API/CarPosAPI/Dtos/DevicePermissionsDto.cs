namespace CarPosAPI.Dtos;

/// <summary>
/// What the authenticated caller may do with a device, projected from their
/// active <see cref="Data.Entities.Access"/> row.
///
/// These flags are <strong>UX hints only</strong>. They exist so the dashboard can
/// grey out a button instead of offering an action that will fail. Every mutation
/// is independently re-authorised server-side against the same grant — a client
/// that lies about its permissions gets a 403, not an effect.
/// </summary>
/// <param name="CanRead">May list the device and read its positions (always true here).</param>
/// <param name="CanDelete">May soft-delete the device.</param>
/// <param name="CanShare">May grant, change and revoke other users' access.</param>
/// <param name="CanModifySettings">May change settings and read the firmware config block.</param>
public sealed record DevicePermissionsDto(
    bool CanRead,
    bool CanDelete,
    bool CanShare,
    bool CanModifySettings);
