namespace CarPosAPI.Services.Privacy;

/// <summary>
/// One journey — a named group of a device's trips, such as a holiday — as it
/// appears in a data export.
///
/// A journey's date range is personal data in its own right: it says when the
/// vehicle's users were somewhere worth naming, whether or not any trip falls in it.
/// </summary>
/// <param name="Id">The journey's id; trips refer to it.</param>
/// <param name="ParentId">The journey it is nested in, or null at the top level.</param>
/// <param name="Name">The journey's name.</param>
/// <param name="RangeFrom">Start of the date range whose trips join it automatically (UTC), or null.</param>
/// <param name="RangeTo">End of that range, exclusive (UTC), or null.</param>
/// <param name="CreatedAt">When the journey was created (UTC).</param>
/// <param name="UpdatedAt">When it was last changed (UTC).</param>
public sealed record JourneyExportRow(
    Guid Id,
    Guid? ParentId,
    string Name,
    DateTime? RangeFrom,
    DateTime? RangeTo,
    DateTime CreatedAt,
    DateTime UpdatedAt);
