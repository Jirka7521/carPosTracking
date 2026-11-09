using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST</c> (create) and <c>PUT</c> (replace) on
/// <c>/api/devices/{deviceId}/journeys</c>.
/// </summary>
/// <param name="Name">Display name, unique among its siblings.</param>
/// <param name="ParentId">The journey to nest it in, or null for the top level.</param>
/// <param name="RangeFrom">
/// Inclusive start of the auto-include range (UTC), or null for no range. The dashboard
/// sends local midnight of the first day.
/// </param>
/// <param name="RangeTo">Exclusive end of the range (UTC) — local midnight after the last day.</param>
/// <param name="IncludePinned">
/// True to also move trips in the range that the user had placed by hand elsewhere — the
/// explicit "every trip between these dates goes here". Ignored without a range.
/// </param>
public sealed record JourneySaveRequestDto(
    [Required]
    [StringLength(TripRules.MaxNameLength, MinimumLength = 1)]
    string Name,
    Guid? ParentId,
    DateTime? RangeFrom,
    DateTime? RangeTo,
    bool IncludePinned);
