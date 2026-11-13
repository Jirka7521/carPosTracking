namespace CarPosAPI.Data.Entities;

/// <summary>
/// One row of the <c>trip_effective_journeys</c> view: a stored trip together with its
/// successor's start and <b>the journey it is in right now</b>, decided entirely in SQL.
///
/// <para>
/// <b>The rule.</b> A pinned trip is in the journey stored on it. Otherwise the best
/// location rule in force at its start whose circle holds its start or end wins (an end
/// match before a start match, then the smaller circle, then the newer rule); otherwise the
/// innermost journey whose date range holds its start; otherwise none. Nothing here is
/// ever written back, so editing a rule or a range, or late data, is reflected by the next
/// read.
/// </para>
///
/// <para>
/// Keyless and read-only: the view is created by raw SQL in the
/// <c>AddJourneyLocationRules</c> migration, and EF never migrates a view-mapped entity.
/// </para>
/// Mapped by <see cref="Configurations.TripEffectiveJourneyConfiguration"/>.
/// </summary>
public sealed class TripEffectiveJourney
{
    /// <summary>The trip's id.</summary>
    public long TripId { get; set; }

    /// <summary>The owning device.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>The stored start.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>The stored end, or null while undecided.</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>The device's next trip start, or null for the newest trip.</summary>
    public DateTime? NextStart { get; set; }

    /// <summary>Whether the user placed the trip by hand.</summary>
    public bool JourneyPinned { get; set; }

    /// <summary>The user's name for the trip.</summary>
    public string? Name { get; set; }

    /// <summary>The journey the trip is in, or null.</summary>
    public Guid? EffectiveJourneyId { get; set; }

    /// <summary>One of <see cref="Dtos.TripJourneySources"/>, or null when in no journey and unpinned.</summary>
    public string? JourneySource { get; set; }

    /// <summary>The location rule that placed the trip, or null.</summary>
    public Guid? LocationRuleId { get; set; }

    /// <summary>True when that rule matched the trip's end, false for its start, null without a rule.</summary>
    public bool? LocationRuleAtEnd { get; set; }
}
