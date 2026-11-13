namespace CarPosAPI.Dtos;

/// <summary>
/// What a location rule would catch, for the rule dialog's live preview — computed in
/// the database with the same <c>trip_near</c> function the journey rule uses.
/// </summary>
/// <param name="TripCount">Trips since the valid-from date that start or end in the circle (per the ticked kinds).</param>
/// <param name="StartCount">Of those, the trips that start there.</param>
/// <param name="EndCount">Of those, the trips that end there. A trip can count in both.</param>
/// <param name="PinnedCount">Of those, trips placed by hand — they stay where they are.</param>
/// <param name="OtherRuleCount">
/// Of those, trips another location rule places right now; the order of precedence
/// decides which rule keeps them.
/// </param>
/// <param name="Points">
/// The matched starts and ends to draw on the map, newest trips first, at most
/// <see cref="TripRules.MaxLocationRulePreviewPoints"/>.
/// </param>
/// <param name="PointsTruncated">True when more matched points exist than were returned.</param>
public sealed record LocationRulePreviewDto(
    int TripCount,
    int StartCount,
    int EndCount,
    int PinnedCount,
    int OtherRuleCount,
    IReadOnlyList<LocationRulePreviewPointDto> Points,
    bool PointsTruncated);
