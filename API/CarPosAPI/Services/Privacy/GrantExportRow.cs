namespace CarPosAPI.Services.Privacy;

/// <summary>
/// One access grant as it appears in a data export — flattened to the device's
/// MQTT identity, since the internal row Guid means nothing outside the database.
/// </summary>
/// <param name="DeviceId">The device the grant is on.</param>
/// <param name="UserId">The account the grant belongs to.</param>
/// <param name="GrantedBy">Who created it, or null if that account has been erased.</param>
/// <param name="IsActive">False once revoked.</param>
/// <param name="GrantedAt">When it was created (UTC).</param>
/// <param name="CanRead">May list the device and read its positions.</param>
/// <param name="CanDelete">May deactivate the device and erase its history.</param>
/// <param name="CanShare">May grant and revoke other users' access.</param>
/// <param name="CanModifySettings">May change device settings.</param>
public sealed record GrantExportRow(
    string DeviceId,
    int UserId,
    int? GrantedBy,
    bool IsActive,
    DateTime GrantedAt,
    bool CanRead,
    bool CanDelete,
    bool CanShare,
    bool CanModifySettings);
