namespace CarPosAPI.Dtos;

/// <summary>
/// A share link as its creator sees it.
///
/// <b>What is absent is the contract.</b> There is no selector, no verifier and no
/// passphrase on this record, and there is no endpoint that can produce them for
/// an existing link — the secrets appear exactly once, in
/// <see cref="ShareLinkCreatedDto"/>, and are unrecoverable afterwards by
/// construction rather than by policy. A creator who has lost the code reissues;
/// that is the whole recovery story, and it is deliberate.
/// </summary>
/// <param name="Id">The link's id, used to revoke it.</param>
/// <param name="DeviceId">MQTT identity of the shared device. Owner-side only — a visitor never sees it.</param>
/// <param name="Label">What the visitor sees the tracker called, and the creator's own note.</param>
/// <param name="ValidFrom">Start of the window (UTC).</param>
/// <param name="ValidUntil">End of the window (UTC).</param>
/// <param name="Scope">One of <see cref="ShareScopeNames"/>.</param>
/// <param name="IncludeSpeed">Whether speed travels with each fix.</param>
/// <param name="IncludeTelemetry">Whether battery and temperature travel with each fix.</param>
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
    bool IncludeTelemetry,
    string Status,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    int SuccessfulRedeems,
    DateTime? LastAccessedAt,
    int FailedAttempts,
    DateTime? LockedUntil);
