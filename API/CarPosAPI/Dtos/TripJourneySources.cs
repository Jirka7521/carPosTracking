namespace CarPosAPI.Dtos;

/// <summary>
/// How a trip came to be in its journey — the wire values of
/// <see cref="TripSummaryDto.JourneySource"/>, produced as-is by the
/// <c>trip_effective_journeys</c> view. Listed in order of precedence.
/// </summary>
public static class TripJourneySources
{
    /// <summary>The user placed it by hand (including "in no journey").</summary>
    public const string Manual = "manual";

    /// <summary>A location rule matched where it started or ended.</summary>
    public const string LocationRule = "locationRule";

    /// <summary>A journey's date range holds its start.</summary>
    public const string DateRange = "dateRange";
}
