namespace CarPosAPI.Services.Trips;

/// <summary>The outcome of <see cref="TripMetadataMatcher.Match"/>.</summary>
/// <param name="Matches">One entry per detected start, ascending.</param>
/// <param name="RemovedTripIds">Existing rows no detected start took over; to be deleted.</param>
internal sealed record TripMatchResult(IReadOnlyList<TripMatch> Matches, IReadOnlyList<long> RemovedTripIds);
