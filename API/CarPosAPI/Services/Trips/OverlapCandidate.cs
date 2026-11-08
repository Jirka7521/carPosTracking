namespace CarPosAPI.Services.Trips;

/// <summary>An (existing trip, detected start) pair whose spans overlap, for <see cref="TripMetadataMatcher"/>.</summary>
/// <param name="Old">The existing trip.</param>
/// <param name="NewIndex">Index of the detected start in the sorted list.</param>
/// <param name="OverlapTicks">How much the two spans overlap, in ticks.</param>
internal sealed record OverlapCandidate(StoredTripSnapshot Old, int NewIndex, long OverlapTicks);
