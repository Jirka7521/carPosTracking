using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// The place a journey's dialog sets, saved with the journey in the same transaction
/// (<see cref="JourneySaveRequestDto.Place"/>): a location rule whose trips join this
/// journey. Like the journey's dates it is a live rule — it applies to every trip since
/// <paramref name="ValidFrom"/>, past ones too.
/// </summary>
/// <param name="RuleId">
/// The journey's own rule in force that this replaces, or null to add a new rule. Another
/// journey's rule, or an ended one, is refused.
/// </param>
/// <param name="Name">Optional label ("Office"); blank means none.</param>
/// <param name="Latitude">Centre latitude, decimal degrees.</param>
/// <param name="Longitude">Centre longitude, decimal degrees.</param>
/// <param name="RadiusMeters">Radius of the circle in metres.</param>
/// <param name="MatchStart">True to match trips starting in the circle.</param>
/// <param name="MatchEnd">True to match trips ending in the circle. At least one of the two.</param>
/// <param name="ValidFrom">
/// The rule covers trips starting at or after this instant (UTC); may lie in the past.
/// The dashboard sends local midnight of the chosen day.
/// </param>
public sealed record JourneyPlaceDto(
    Guid? RuleId,
    [StringLength(TripRules.MaxNameLength)]
    string? Name,
    [Range(-90.0, 90.0)]
    double Latitude,
    [Range(-180.0, 180.0)]
    double Longitude,
    [Range(TripRules.MinLocationRuleRadiusMeters, TripRules.MaxLocationRuleRadiusMeters)]
    int RadiusMeters,
    bool MatchStart,
    bool MatchEnd,
    [Required]
    DateTime? ValidFrom);
