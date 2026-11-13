namespace CarPosAPI.Services.Privacy;

/// <summary>
/// One location rule — a circle on the map whose trips join a journey — as it appears
/// in a data export, ended ones included.
///
/// A rule is personal data in its own right: its centre is a place the vehicle's users
/// chose to name, typically home or work.
/// </summary>
/// <param name="Id">The rule's id.</param>
/// <param name="JourneyId">The journey its trips join; null once that journey was deleted.</param>
/// <param name="Name">The rule's label, if one was given.</param>
/// <param name="Latitude">Centre latitude, decimal degrees.</param>
/// <param name="Longitude">Centre longitude, decimal degrees.</param>
/// <param name="RadiusMeters">Radius of the circle in metres.</param>
/// <param name="MatchStart">Whether trips starting in the circle match.</param>
/// <param name="MatchEnd">Whether trips ending in the circle match.</param>
/// <param name="ValidFrom">From when the rule applies (UTC).</param>
/// <param name="EndedAt">When it was ended (UTC), or null while in force.</param>
/// <param name="EndReason">Why it ended, or null.</param>
/// <param name="CreatedAt">When it was created (UTC).</param>
/// <param name="UpdatedAt">When it was last changed (UTC).</param>
public sealed record LocationRuleExportRow(
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
    DateTime UpdatedAt);
