namespace CarPosAPI.Services.Trips;

/// <summary>What <see cref="JourneyTreeRules.CheckPlacement"/> can object to.</summary>
internal enum JourneyPlacementProblem
{
    /// <summary>The intended parent does not exist on this device.</summary>
    NoSuchJourney,

    /// <summary>The journey would end up inside itself.</summary>
    Cycle,

    /// <summary>The journey (or its deepest sub-journey) would sit too deep.</summary>
    TooDeep,
}
