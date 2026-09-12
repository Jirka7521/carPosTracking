namespace CarPosAPI.Dtos;

/// <summary>
/// A share link as its creator sees it.
///
/// <b>This record carries the link and its code</b>, so a creator who has closed
/// the one-time reveal can look them up again. That follows from the storage
/// decision of 2026-09-12 recorded on <see cref="Data.Entities.ShareLink"/>: both
/// secrets are held in the clear, and withholding them here would be theatre
/// rather than a control.
///
/// <para>
/// It does mean <c>GET /api/shares</c> is a credential-bearing response, so it
/// stays behind <c>[Authorize]</c> and <c>CanShare</c> on the device, exactly as
/// before — and it is still kept out of the GDPR data export, where it would end
/// up in a file that leaves the system entirely.
/// </para>
/// </summary>
/// <param name="Id">The link's id, used to revoke it.</param>
/// <param name="DeviceId">MQTT identity of the shared device. Owner-side only — a visitor never sees it.</param>
/// <param name="Label">What the visitor sees the tracker called, and the creator's own note.</param>
/// <param name="Token">The <c>selector.verifier</c> secret from the URL, for re-displaying the link.</param>
/// <param name="Passphrase">The visitor's code, in its grouped display form.</param>
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
    string Token,
    string Passphrase,
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
