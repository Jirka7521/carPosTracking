namespace CarPosAPI.Services.Trips;

/// <summary>A location rule as drawn so far in the dialog — what to preview.</summary>
/// <param name="Latitude">Centre latitude, decimal degrees.</param>
/// <param name="Longitude">Centre longitude, decimal degrees.</param>
/// <param name="RadiusMeters">Radius of the circle in metres.</param>
/// <param name="MatchStart">True to count trips starting in the circle.</param>
/// <param name="MatchEnd">True to count trips ending in the circle.</param>
/// <param name="ValidFrom">Only trips starting at or after this instant count.</param>
/// <param name="ExcludeRuleId">
/// The rule being edited, if any — its own trips are not "placed by another rule".
/// </param>
public sealed record LocationRulePreviewQuery(
    double Latitude,
    double Longitude,
    int RadiusMeters,
    bool MatchStart,
    bool MatchEnd,
    DateTime ValidFrom,
    Guid? ExcludeRuleId);
