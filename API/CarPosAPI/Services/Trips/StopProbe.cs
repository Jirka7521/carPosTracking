namespace CarPosAPI.Services.Trips;

/// <summary>The answer to "is the fix at this index the first fix of a stop?".</summary>
/// <param name="IsStop">
/// True or false when decidable; null when the fixes ran out before the question could
/// be answered and more exist beyond them (the caller must load more).
/// </param>
/// <param name="LastIndex">Index of the last fix still within the stop radius of the anchor.</param>
/// <param name="ReachedEnd">True when that run of nearby fixes reached the end of the list.</param>
internal sealed record StopProbe(bool? IsStop, int LastIndex, bool ReachedEnd);
