namespace CarPosAPI.Services.Trips;

/// <summary>
/// A stored trip with everything the database can say about it in one query: its
/// successor's start (the bound for rebuilding it) and the journey it is in right now.
///
/// <para>
/// A class with init-only properties rather than a positional record, because queries
/// keep composing on it — filtering and ordering by <see cref="EffectiveJourneyId"/> —
/// and EF Core can only translate member access on a projection built with an object
/// initializer.
/// </para>
/// </summary>
internal sealed class TripRow
{
    /// <summary>Trip id.</summary>
    public long Id { get; init; }

    /// <summary>The stored start.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>The device's next trip start, or null for the newest trip.</summary>
    public DateTime? NextStart { get; init; }

    /// <summary>The pinned journey, or else the innermost journey whose range holds the start, or null.</summary>
    public Guid? EffectiveJourneyId { get; init; }

    /// <summary>Whether the user placed the trip by hand.</summary>
    public bool JourneyPinned { get; init; }

    /// <summary>The user's name for it.</summary>
    public string? Name { get; init; }
}
