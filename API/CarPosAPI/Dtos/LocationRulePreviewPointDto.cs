namespace CarPosAPI.Dtos;

/// <summary>One matched trip start or end, for drawing on the rule dialog's map.</summary>
/// <param name="TripId">The trip.</param>
/// <param name="Kind"><c>start</c> or <c>end</c>.</param>
/// <param name="Latitude">The fix that matched, decimal degrees.</param>
/// <param name="Longitude">The fix that matched, decimal degrees.</param>
public sealed record LocationRulePreviewPointDto(long TripId, string Kind, double Latitude, double Longitude);
