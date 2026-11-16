using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST</c> (create) and <c>PUT</c> (replace) on
/// <c>/api/devices/{deviceId}/location-rules</c>. Only a rule in force can be replaced;
/// the change applies to every trip since <paramref name="ValidFrom"/>, past ones too.
/// </summary>
/// <param name="JourneyId">The journey matching trips join.</param>
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
public sealed record LocationRuleSaveRequestDto(
    [Required]
    Guid? JourneyId,
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
