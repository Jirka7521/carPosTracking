using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>PUT /api/devices/{deviceId}/trips/{tripId}</c>. Names a trip; an
/// empty or whitespace-only value clears the name.
/// </summary>
/// <param name="Name">The new name, or an empty string to remove it.</param>
public sealed record TripRenameRequestDto(
    [Required(AllowEmptyStrings = true)]
    [StringLength(TripRules.MaxNameLength)]
    string Name);
