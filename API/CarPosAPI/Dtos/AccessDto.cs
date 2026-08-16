namespace CarPosAPI.Dtos;

/// <summary>
/// One sharing grant as returned by <c>GET /api/access?deviceId=</c> and by the
/// create/update endpoints — a row in the device's "people with access" list.
/// Only active grants are ever returned; revoked ones stay in the database for
/// the audit trail and are invisible here.
/// </summary>
/// <param name="Id">Surrogate key; the value <c>PUT</c>/<c>DELETE /api/access/{id}</c> address.</param>
/// <param name="UserId">The user the grant belongs to.</param>
/// <param name="DeviceId">MQTT identity of the device the grant is on.</param>
/// <param name="GrantedBy">Id of the user who created the grant (audit).</param>
/// <param name="DateRegistration">When the grant was created (UTC).</param>
/// <param name="CanRead">Always true on an active grant — the invariant is enforced on write.</param>
/// <param name="CanDelete">May soft-delete the device.</param>
/// <param name="CanShare">May re-share the device; implies <paramref name="CanModifySettings"/>.</param>
/// <param name="CanModifySettings">May change settings and read the firmware block.</param>
public sealed record AccessDto(
    int Id,
    int UserId,
    string DeviceId,
    int GrantedBy,
    DateTime DateRegistration,
    bool CanRead,
    bool CanDelete,
    bool CanShare,
    bool CanModifySettings);
