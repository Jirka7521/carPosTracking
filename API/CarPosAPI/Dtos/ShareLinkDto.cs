namespace CarPosAPI.Dtos;

/// <summary>
/// A share link as its creator sees it.
///
/// <b>It carries no secret.</b> The link and its code are stored only as digests
/// and are shown once, in the <see cref="ShareLinkCreatedDto"/> that creates or
/// reissues a link. Anyone who can share the device used to be able to read every
/// live link again here; now a lost link is reissued, not looked up.
/// </summary>
/// <param name="Id">The link's id, used to revoke it.</param>
/// <param name="DeviceId">MQTT identity of the shared device. Owner-side only — a visitor never sees it.</param>
/// <param name="Label">What the visitor sees the tracker called, and the creator's own note.</param>
/// <param name="ValidFrom">Start of the window (UTC).</param>
/// <param name="ValidUntil">End of the window (UTC).</param>
/// <param name="Scope">One of <see cref="ShareScopeNames"/>.</param>
/// <param name="IncludeSpeed">Whether speed travels with each fix.</param>
/// <param name="IncludeBattery">Whether the battery percentage travels with each fix.</param>
/// <param name="Status">One of <see cref="ShareLinkStatusNames"/>, derived server-side.</param>
/// <param name="CreatedAt">When the link was minted (UTC).</param>
/// <param name="RevokedAt">When it was withdrawn (UTC), or null.</param>
/// <param name="SuccessfulRedeems">How many times the code was entered correctly.</param>
/// <param name="LastAccessedAt">When it was last opened successfully (UTC), or null.</param>
/// <param name="FailedAttempts">
/// Consecutive wrong-code attempts since the last success. Surfaced so that a link
/// somebody is working on is visible to its creator rather than silent — the
/// counter is the only signal they get, since nothing about the visitor is stored.
/// </param>
/// <param name="LockedUntil">While in the future, the link is refusing codes (UTC).</param>
public sealed record ShareLinkDto(
    Guid Id,
    string DeviceId,
    string Label,
    DateTime ValidFrom,
    DateTime ValidUntil,
    string Scope,
    bool IncludeSpeed,
    bool IncludeBattery,
    string Status,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    int SuccessfulRedeems,
    DateTime? LastAccessedAt,
    int FailedAttempts,
    DateTime? LockedUntil);
