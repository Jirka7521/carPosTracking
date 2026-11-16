namespace CarPosAPI.Dtos;

/// <summary>
/// One location rule of a device — in force or ended (its history). Every unpinned trip
/// that starts or ends within <paramref name="RadiusMeters"/> of the point, and starts in
/// <c>[ValidFrom, EndedAt)</c>, is in <paramref name="JourneyId"/> (unless an earlier rule
/// in the order of precedence claims it).
/// </summary>
/// <param name="Id">Rule id.</param>
/// <param name="JourneyId">
/// The journey matching trips join. Null only on an ended rule whose journey was deleted
/// at the top level; its end reason is then <see cref="LocationRuleEndReasons.JourneyDeleted"/>,
/// or <see cref="LocationRuleEndReasons.User"/> if it had already ended.
/// </param>
/// <param name="Name">Optional label, or null.</param>
/// <param name="Latitude">Centre latitude, decimal degrees.</param>
/// <param name="Longitude">Centre longitude, decimal degrees.</param>
/// <param name="RadiusMeters">Radius of the circle in metres.</param>
/// <param name="MatchStart">True when trips starting in the circle match.</param>
/// <param name="MatchEnd">True when trips ending in the circle match.</param>
/// <param name="ValidFrom">The rule covers trips starting at or after this instant (UTC).</param>
/// <param name="EndedAt">When the rule ended (UTC, exclusive), or null while in force.</param>
/// <param name="EndReason">One of <see cref="LocationRuleEndReasons"/>, or null while in force.</param>
/// <param name="CreatedAt">When the rule was created (UTC).</param>
/// <param name="UpdatedAt">When it was last changed (UTC).</param>
/// <param name="TripCount">How many trips the rule places right now.</param>
public sealed record LocationRuleDto(
    Guid Id,
    Guid? JourneyId,
    string? Name,
    double Latitude,
    double Longitude,
    int RadiusMeters,
    bool MatchStart,
    bool MatchEnd,
    DateTime ValidFrom,
    DateTime? EndedAt,
    string? EndReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int TripCount);
