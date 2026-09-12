using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request to change an existing share link. A <b>full replacement</b> of the
/// editable fields, not a patch — the same convention as
/// <see cref="AccessUpdateRequestDto"/>, so a client that omits a field is saying
/// "off", never "leave it".
///
/// <para>
/// <b>What is absent cannot be changed.</b> There is no device here: a link points
/// at the tracker it was minted for, and re-pointing one would silently hand a
/// recipient a different vehicle. There are no secrets either — the link and its
/// code are unrecoverable by construction, so "change the code" is not an edit,
/// it is a new link.
/// </para>
///
/// <para>
/// <b>The window is the consequential part.</b> It bounds which fixes the link can
/// return as well as when it works, so moving <paramref name="ValidFrom"/> earlier
/// retroactively widens what a recipient who already holds the link can see. That
/// is the creator's decision to make, and the UI says so plainly rather than
/// letting it happen as a side effect of renaming something.
/// </para>
/// </summary>
/// <param name="Label">What the visitor sees the tracker called. Blank falls back to the device's display name.</param>
/// <param name="ValidFrom">New start of the window (UTC).</param>
/// <param name="ValidUntil">New end of the window (UTC).</param>
/// <param name="Scope">One of <see cref="ShareScopeNames"/>.</param>
/// <param name="IncludeSpeed">Whether speed travels with each fix.</param>
/// <param name="IncludeTelemetry">Whether battery and temperature travel with each fix.</param>
public sealed record ShareLinkUpdateRequestDto(
    [StringLength(80)]
    string? Label,

    [Required]
    DateTime ValidFrom,

    [Required]
    DateTime ValidUntil,

    [Required]
    [StringLength(32, MinimumLength = 1)]
    string Scope,

    bool IncludeSpeed,

    bool IncludeTelemetry);
