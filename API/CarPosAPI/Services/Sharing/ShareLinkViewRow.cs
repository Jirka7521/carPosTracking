using CarPosAPI.Data.Entities;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// The columns <see cref="ShareViewService"/> needs to decide what an anonymous
/// visitor may see.
///
/// <para>
/// A named projection rather than loading the entity, for two reasons. It keeps
/// the three secret columns out of the query on a path that runs on every refresh
/// of a public page. And it makes the set of facts this decision rests on
/// enumerable in one place — window, revocation, scope, two flags and the device
/// row — rather than implied by whichever properties the code happens to touch.
/// </para>
/// </summary>
/// <param name="DeviceId">Internal row id of the shared device — never leaves the server.</param>
/// <param name="Label">What the visitor sees the tracker called.</param>
/// <param name="ValidFrom">Start of the window (UTC).</param>
/// <param name="ValidUntil">End of the window (UTC).</param>
/// <param name="Scope">How much history the link exposes.</param>
/// <param name="IncludeSpeed">Whether speed travels with each fix.</param>
/// <param name="IncludeTelemetry">Whether battery and temperature travel with each fix.</param>
/// <param name="RevokedAt">When the link was withdrawn (UTC), or null while it stands.</param>
internal sealed record ShareLinkViewRow(
    Guid DeviceId,
    string Label,
    DateTime ValidFrom,
    DateTime ValidUntil,
    ShareScope Scope,
    bool IncludeSpeed,
    bool IncludeTelemetry,
    DateTime? RevokedAt);
