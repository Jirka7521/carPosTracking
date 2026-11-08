namespace CarPosAPI.Services.Trips;

/// <summary>How a trip ended — derived on every read, never stored.</summary>
internal enum TripEndKind
{
    /// <summary>The car stopped for at least the minimum stop: a real arrival.</summary>
    Stay,

    /// <summary>
    /// The data stopped: a long silence across which the car moved, so what happened in
    /// between is unknown and the trip is cut at the last fix before it.
    /// </summary>
    Gap,

    /// <summary>No stop has followed yet — the trip may still be going.</summary>
    Open,
}
