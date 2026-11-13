namespace CarPosAPI.Services.Privacy;

/// <summary>
/// One detected trip of a readable device as it appears in a data export: exactly
/// what is stored about it, and nothing derived.
///
/// A trip row keeps only its start and end time and the user's organisation of it (a
/// name, and a placement made by hand). Its distance, duration and speeds are
/// recomputed from the positions whenever they are shown, so they are not stored
/// anywhere to export; the positions they come from are in the same document.
/// </summary>
/// <param name="Id">The trip's id, as it appears in the dashboard's trip URLs.</param>
/// <param name="StartTime">When the trip started (UTC).</param>
/// <param name="EndTime">When it ended (UTC), or null while that is not yet known.</param>
/// <param name="JourneyId">
/// The journey the trip was placed in by hand; null when it was not, or was taken
/// out of every journey by hand (see <paramref name="JourneyPinned"/>).
/// </param>
/// <param name="JourneyPinned">
/// True when the trip's journey was chosen by hand rather than by a location rule or a
/// journey's date range.
/// </param>
/// <param name="Name">The trip's name, if one was given.</param>
public sealed record TripExportRow(
    long Id,
    DateTime StartTime,
    DateTime? EndTime,
    Guid? JourneyId,
    bool JourneyPinned,
    string? Name);
