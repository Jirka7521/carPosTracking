namespace CarPosAPI.Services.Trips;

/// <summary>
/// The one row the preview's counting query returns. Property names are the SQL column
/// aliases (<c>Database.SqlQuery</c> maps by name), so keep them in step with
/// <see cref="LocationRuleService.BuildPreviewCountsQuery"/>.
/// </summary>
internal sealed class LocationRulePreviewCounts
{
    /// <summary>Matched trips.</summary>
    public int TripCount { get; init; }

    /// <summary>Matched by where they start.</summary>
    public int StartCount { get; init; }

    /// <summary>Matched by where they end.</summary>
    public int EndCount { get; init; }

    /// <summary>Matched but placed by hand.</summary>
    public int PinnedCount { get; init; }

    /// <summary>Matched but placed by another location rule right now.</summary>
    public int OtherRuleCount { get; init; }
}
