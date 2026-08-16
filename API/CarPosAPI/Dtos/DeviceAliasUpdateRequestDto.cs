using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>PUT /api/me/devices/{deviceId}/alias</c>. Sets the caller's
/// private nickname for a device; an empty or whitespace-only value <em>clears</em>
/// it, so the endpoint is a single idempotent "set to this" rather than a
/// PUT/DELETE pair the UI would have to choose between.
/// </summary>
/// <param name="Alias">The new nickname, or an empty string to remove it.</param>
public sealed record DeviceAliasUpdateRequestDto(
    [Required(AllowEmptyStrings = true)]
    [StringLength(128)]
    string Alias);
