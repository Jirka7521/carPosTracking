namespace CarPosAPI.Dtos;

/// <summary>
/// Response of <c>DELETE /api/me</c>: what the erasure actually removed.
///
/// A bare 204 would be a worse answer. Erasure is irreversible and its scope is
/// not obvious — a device somebody else still uses survives, one only you could
/// see does not — so the person who just triggered it is told exactly what went.
/// </summary>
/// <param name="DevicesDeleted">Devices removed outright, because nobody else could see them.</param>
/// <param name="DevicesRetained">Devices left in place, because they are still shared with somebody.</param>
/// <param name="PositionsDeleted">Position rows erased with those devices.</param>
/// <param name="GrantsDeleted">The account's own access grants, removed.</param>
/// <param name="GrantsAnonymised">Other people's grants whose "granted by" reference was cleared.</param>
/// <param name="ShareLinksDeleted">Temporary share links this account had created, destroyed with it.</param>
public sealed record AccountErasureResultDto(
    int DevicesDeleted,
    int DevicesRetained,
    long PositionsDeleted,
    int GrantsDeleted,
    int GrantsAnonymised,
    int ShareLinksDeleted);
