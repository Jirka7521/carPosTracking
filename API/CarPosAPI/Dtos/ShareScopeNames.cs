namespace CarPosAPI.Dtos;

/// <summary>
/// The wire spellings of <see cref="Data.Entities.ShareScope"/>.
///
/// <c>const</c> strings rather than a converter, matching
/// <see cref="ConfigRevisionSourceNames"/> and for the same reason: the value is
/// produced inside an EF Core <c>Select</c>, where only a literal survives
/// translation to SQL.
/// </summary>
public static class ShareScopeNames
{
    /// <summary>Only the newest fix inside the window.</summary>
    public const string LatestOnly = "latestOnly";

    /// <summary>Every fix inside the window, up to the row cap.</summary>
    public const string FullTrack = "fullTrack";
}
