namespace CarPosAPI.Services.Trips;

/// <summary>
/// One matched trip from the preview's points query: the fix that matched at its start
/// and/or end. Property names are the SQL column aliases (<c>Database.SqlQuery</c> maps by
/// name), so keep them in step with <see cref="LocationRuleService.BuildPreviewPointsQuery"/>.
/// </summary>
internal sealed class LocationRulePreviewRow
{
    /// <summary>The trip.</summary>
    public long TripId { get; init; }

    /// <summary>Latitude of the start (or parking) fix inside the circle, or null when the start did not match.</summary>
    public double? StartLatitude { get; init; }

    /// <summary>Longitude of that fix, or null.</summary>
    public double? StartLongitude { get; init; }

    /// <summary>Latitude of the end fix inside the circle, or null when the end did not match.</summary>
    public double? EndLatitude { get; init; }

    /// <summary>Longitude of that fix, or null.</summary>
    public double? EndLongitude { get; init; }
}
