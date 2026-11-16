namespace CarPosAPI.Services.Trips;

/// <summary>How many trips one location rule places right now — one row of a grouped count.</summary>
/// <param name="RuleId">The rule.</param>
/// <param name="TripCount">Trips it places.</param>
internal sealed record LocationRuleTripCount(Guid RuleId, int TripCount);
