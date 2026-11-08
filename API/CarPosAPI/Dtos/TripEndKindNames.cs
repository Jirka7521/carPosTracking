namespace CarPosAPI.Dtos;

/// <summary>
/// The wire words for how a trip ended (<c>TripSummaryDto.EndKind</c>), mirrored by the
/// FE's <c>TripEndKind</c> type.
/// </summary>
public static class TripEndKindNames
{
    /// <summary>The car stopped for at least the minimum stop.</summary>
    public const string Stay = "stay";

    /// <summary>The data stopped: a long silence across which the car moved.</summary>
    public const string Gap = "gap";

    /// <summary>No stop has followed yet.</summary>
    public const string Open = "open";
}
