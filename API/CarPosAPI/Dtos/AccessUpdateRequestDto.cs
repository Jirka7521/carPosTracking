namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>PUT /api/access/{id}</c> — overwrite the capability set on
/// an existing grant. All three flags are always sent: a PUT replaces the set, so
/// omitting one means "off", not "unchanged". That keeps a stale checkbox in an
/// old browser tab from silently re-granting something.
///
/// <c>CanRead</c> is absent by design; revoking read access is
/// <c>DELETE /api/access/{id}</c>, not a flag.
/// </summary>
/// <param name="CanDelete">Whether they may soft-delete the device.</param>
/// <param name="CanShare">Whether they may re-share it; coerces <paramref name="CanModifySettings"/> on.</param>
/// <param name="CanModifySettings">Whether they may change settings and read the firmware block.</param>
public sealed record AccessUpdateRequestDto(
    bool CanDelete = false,
    bool CanShare = false,
    bool CanModifySettings = false);
