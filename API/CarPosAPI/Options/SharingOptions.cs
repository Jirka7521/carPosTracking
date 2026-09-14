using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// Limits on temporary share links, bound from the <c>Sharing</c> configuration
/// section. Nothing here is secret — these are policy ceilings, and they live in
/// configuration so a deployment can tighten them without a rebuild.
/// </summary>
public sealed class SharingOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Sharing";

    /// <summary>
    /// Longest window a single link may cover, in days.
    ///
    /// The ceiling exists because the failure mode of this feature is not a broken
    /// link, it is a forgotten one: a share created "just for now" with an end date
    /// years out is a permanent public tracker that nobody remembers making. Thirty
    /// days is long enough for any plausible use and short enough that an
    /// overlooked link dies on its own.
    /// </summary>
    [Range(1, 365)]
    public int MaxWindowDays { get; set; } = 30;

    /// <summary>
    /// How many live links one device may carry at once.
    ///
    /// A cap rather than none, because every live link is another copy of the same
    /// location data in circulation, and because an unbounded count turns one
    /// compromised account into a sprayer of shares. Revoked and expired links do
    /// not count against it.
    /// </summary>
    [Range(1, 100)]
    public int MaxLiveLinksPerDevice { get; set; } = 10;
}
