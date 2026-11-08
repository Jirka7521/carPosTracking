namespace CarPosAPI.Services.Trips;

/// <summary>What <see cref="TripDetector.Detect"/> found in one window of fixes.</summary>
/// <param name="Starts">Start times of the trips decided in this window, ascending.</param>
/// <param name="ResumeFrom">
/// The latest <b>restart point</b> the scan passed — the first fix of a confirmed stop, or
/// the first fix after a hard gap — or null if it passed none. A scan restarted there
/// reproduces this one exactly. When <paramref name="IsComplete"/> is false, every start
/// before it is final and the next pass must resume from it.
/// </param>
/// <param name="IsComplete">
/// False when the window ended before the scan could decide everything in it (more
/// fixes exist beyond it).
/// </param>
internal sealed record TripDetectionResult(IReadOnlyList<DateTime> Starts, DateTime? ResumeFrom, bool IsComplete);
