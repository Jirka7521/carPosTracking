namespace CarPosAPI.Dtos;

/// <summary>
/// Why a location rule stopped being in force — the wire values of
/// <see cref="LocationRuleDto.EndReason"/>, stored as-is in
/// <c>journey_location_rules.end_reason</c> (and CHECK-constrained there).
/// </summary>
public static class LocationRuleEndReasons
{
    /// <summary>A user ended it.</summary>
    public const string User = "user";

    /// <summary>Its top-level target journey was deleted, so there was nowhere left to put trips.</summary>
    public const string JourneyDeleted = "journeyDeleted";
}
