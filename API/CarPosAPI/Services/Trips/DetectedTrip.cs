namespace CarPosAPI.Services.Trips;

/// <summary>One trip <see cref="TripDetector"/> decided: what the database keeps of it.</summary>
/// <param name="Start">Fix time of its first moving fix.</param>
/// <param name="End">
/// Fix time of its last fix — the first fix of the stop that ended it, or the last fix
/// before a hard gap — or null when it is still open (the newest trip, until a later fix
/// confirms the stop). <see cref="TripSegmentAnalyzer"/> finds the same end on read.
/// </param>
internal sealed record DetectedTrip(DateTime Start, DateTime? End);
