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
/// <param name="Place">
/// A place whose trips join this journey — a location rule added, or one of this
/// journey's rules in force replaced — saved in the same transaction: if the place is
/// refused, the journey is not saved either. Null (or absent) leaves the journey's
/// location rules as they are.
/// </param>
/// <param name="EndPlaceRuleId">
/// One of this journey's rules in force to end, in the same transaction: it stops placing
/// new trips and keeps the ones it placed. Not together with <paramref name="Place"/>.
/// </param>
public sealed record JourneySaveRequestDto(
    [Required]
    [StringLength(TripRules.MaxNameLength, MinimumLength = 1)]
    string Name,
    Guid? ParentId,
    DateTime? RangeFrom,
    DateTime? RangeTo,
    bool IncludePinned,
    JourneyPlaceDto? Place = null,
    Guid? EndPlaceRuleId = null);
