namespace CarPosAPI.Services.Privacy;

/// <summary>
/// What an account erasure actually removed. Returned so the caller can be told
/// plainly what happened rather than just "done" — erasure is irreversible, and a
/// person who has just triggered one deserves to see the shape of it.
/// </summary>
/// <param name="DevicesDeleted">Devices removed outright, because nobody else could see them.</param>
/// <param name="DevicesRetained">Devices left alone, because they are still shared with somebody.</param>
/// <param name="PositionsDeleted">Position rows erased along with the deleted devices.</param>
/// <param name="GrantsDeleted">The erased account's own access grants.</param>
/// <param name="GrantsAnonymised">Grants held by other people whose "granted by" reference was nulled.</param>
/// <param name="ShareLinksDeleted">
/// Temporary share links this account had created, destroyed with it. Unlike a
/// grant handed to another person, a share link is not left standing: it is a live
/// credential with no account behind it any more, and nobody remaining could
/// revoke one.
/// </param>
public sealed record AccountErasureSummary(
    int DevicesDeleted,
    int DevicesRetained,
    long PositionsDeleted,
    int GrantsDeleted,
    int GrantsAnonymised,
    int ShareLinksDeleted);
