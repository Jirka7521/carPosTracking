namespace CarPosAPI.Data.Entities;

/// <summary>
/// One detected trip of a device — deliberately reduced to <b>its start and end time
/// and the user's organisation of it</b>.
///
/// <para>
/// The trip's distance, duration and speeds are never stored: they are derived from
/// <see cref="Position"/> rows on every read by <c>Services.Trips.TripSegmentAnalyzer</c>,
/// using the same stop-finding code that detected the trip. That keeps derived personal
/// data to a minimum, means a change to how a statistic is computed needs no
/// recomputation, and lets nothing go stale. The end <em>time</em> is the one exception:
/// location rules ask "did this trip end here?" in SQL, which needs to know which fix
/// ended it (see <see cref="EndTime"/>).
/// </para>
///
/// <para>
/// Rows are written only by the trip detection worker, which updates them <b>in
/// place</b> when late data moves a start (see <c>TripMetadataMatcher</c>), so the id
/// — and every link to the trip — survives recomputation.
/// </para>
/// Mapped by <see cref="Configurations.TripConfiguration"/>.
/// </summary>
public sealed class Trip
{
    /// <summary>Surrogate key (bigint identity). Stable across recomputes.</summary>
    public long Id { get; set; }

    /// <summary>Owning device (FK, delete restricted, like its positions).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// Fix time (UTC) of the trip's first moving fix — the first fix after the stop
    /// that precedes it. This is exactly where the detector's scan resumes, so the
    /// read path can rebuild the trip from here and reach the same end.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Fix time (UTC) of the trip's last fix: the first fix of the stop that ended it, or
    /// the last fix before a data gap the car moved across. Null while the end is not yet
    /// decided — the newest trip, until the next fix confirms the stop. Written by the
    /// detector together with <see cref="StartTime"/>; the database looks the fix up by
    /// (device, fix time) to test it against location rules.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// The journey the user placed this trip in by hand, or null. Only meaningful when
    /// <see cref="JourneyPinned"/> is true — an unpinned trip's journey is computed from
    /// location rules and date ranges at query time and never stored.
    /// </summary>
    public Guid? JourneyId { get; set; }

    /// <summary>
    /// True when the user decided this trip's placement by hand (including "not in any
    /// journey", stored as a null <see cref="JourneyId"/>). Location rules and date ranges
    /// never move a pinned trip. CHECK: a journey may only be stored on a pinned trip.
    /// </summary>
    public bool JourneyPinned { get; set; }

    /// <summary>Optional user-given name; null when unnamed.</summary>
    public string? Name { get; set; }

    /// <summary>When the detector last wrote this row (UTC).</summary>
    public DateTime ComputedAt { get; set; }
}
