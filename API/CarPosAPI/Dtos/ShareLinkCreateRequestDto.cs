using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request to mint a share link. Validated here for shape; the window's length and
/// the per-device link ceiling are business rules and live in
/// <see cref="Services.Sharing.ShareLinkService"/>.
/// </summary>
/// <param name="DeviceId">MQTT identity of the device to share. The caller must hold <c>CanShare</c> on it.</param>
/// <param name="Label">
/// Optional. What the visitor will see the tracker called. Left empty, the
/// device's display name is used. It is worth setting: it is the visitor's only
/// name for the thing, so it can be "the car" rather than anything identifying.
/// </param>
/// <param name="ValidFrom">Start of the window (UTC).</param>
/// <param name="ValidUntil">End of the window (UTC).</param>
/// <param name="Scope">One of <see cref="ShareScopeNames"/>. Unrecognised values are refused rather than defaulted.</param>
/// <param name="IncludeSpeed">Whether speed travels with each fix.</param>
/// <param name="IncludeTelemetry">Whether battery and temperature travel with each fix.</param>
public sealed record ShareLinkCreateRequestDto(
    [Required]
    [StringLength(64, MinimumLength = 1)]
    string DeviceId,

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
