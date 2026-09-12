using CarPosAPI.Data.Entities;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// The columns of a <see cref="ShareLink"/> that are safe to read.
///
/// <para>
/// It exists so the management query can project in SQL and still have a named
/// type to hand around -- CLAUDE.md's rule against anonymous types, and the reason
/// behind it. It carries the selector and verifier because the creator is entitled
/// to see the link again; the device row id and the internal Guid still stop here.
/// </para>
/// </summary>
/// <param name="Id">The link's id.</param>
/// <param name="Label">What the visitor sees the tracker called.</param>
/// <param name="Selector">Lookup half of the link secret.</param>
/// <param name="Verifier">Authorising half of the link secret.</param>
/// <param name="Passphrase">The visitor's code, in its grouped display form.</param>
/// <param name="ValidFrom">Start of the window (UTC).</param>
/// <param name="ValidUntil">End of the window (UTC).</param>
/// <param name="Scope">How much history the link exposes.</param>
/// <param name="IncludeSpeed">Whether speed travels with each fix.</param>
/// <param name="IncludeTelemetry">Whether battery and temperature travel with each fix.</param>
/// <param name="CreatedAt">When the link was minted (UTC).</param>
/// <param name="RevokedAt">When it was withdrawn (UTC), or null.</param>
/// <param name="SuccessfulRedeems">How many times the code was entered correctly.</param>
/// <param name="LastAccessedAt">When it was last opened successfully (UTC), or null.</param>
/// <param name="FailedAttempts">Consecutive wrong-code attempts since the last success.</param>
/// <param name="LockedUntil">End of any cooldown (UTC), or null.</param>
internal sealed record ShareLinkRow(
    Guid Id,
    string Label,
    string Selector,
    string Verifier,
    string Passphrase,
    DateTime ValidFrom,
    DateTime ValidUntil,
    ShareScope Scope,
    bool IncludeSpeed,
    bool IncludeTelemetry,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    int SuccessfulRedeems,
    DateTime? LastAccessedAt,
    int FailedAttempts,
    DateTime? LockedUntil)
{
    /// <summary>
    /// Builds a row from an entity already in memory, for the create path — which
    /// has just written the link and would otherwise re-read it only to project.
    /// </summary>
    /// <param name="link">The freshly saved link.</param>
    /// <returns>The same values in the projected shape.</returns>
    public static ShareLinkRow From(ShareLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return new ShareLinkRow(
            link.Id,
            link.Label,
            link.Selector,
            link.Verifier,
            link.Passphrase,
            link.ValidFrom,
            link.ValidUntil,
            link.Scope,
            link.IncludeSpeed,
            link.IncludeTelemetry,
            link.CreatedAt,
            link.RevokedAt,
            link.SuccessfulRedeems,
            link.LastAccessedAt,
            link.FailedAttempts,
            link.LockedUntil);
    }
}
