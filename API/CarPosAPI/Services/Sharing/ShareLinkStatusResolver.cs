using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Derives a share link's lifecycle state from its timestamps and counters.
///
/// <para>
/// There is no status column, and this class is why. Every state below is a
/// reading of data that already exists; storing a copy would create a second
/// source of truth that drifts the moment a window passes with nobody looking. The
/// order of the checks is the precedence: a revoked link that has also expired
/// reads as revoked, because the creator's act is the more informative fact.
/// </para>
///
/// Static and clock-free — the caller supplies "now", so every branch is
/// assertable without waiting for one.
/// </summary>
internal static class ShareLinkStatusResolver
{
    /// <summary>Works out how a link should be described to its creator.</summary>
    /// <param name="revokedAt">When it was withdrawn, or null.</param>
    /// <param name="validFrom">Start of its window (UTC).</param>
    /// <param name="validUntil">End of its window (UTC).</param>
    /// <param name="lockedUntil">End of any cooldown (UTC), or null.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>One of <see cref="ShareLinkStatusNames"/>.</returns>
    public static string Resolve(
        DateTime? revokedAt,
        DateTime validFrom,
        DateTime validUntil,
        DateTime? lockedUntil,
        DateTime nowUtc)
    {
        if (revokedAt.HasValue)
        {
            return ShareLinkStatusNames.Revoked;
        }

        if (nowUtc > validUntil)
        {
            return ShareLinkStatusNames.Expired;
        }

        if (nowUtc < validFrom)
        {
            return ShareLinkStatusNames.Scheduled;
        }

        // Only meaningful inside the window: a cooldown on a link nobody can reach
        // any more is not news, and reporting it would bury the reason that matters.
        if (lockedUntil.HasValue && lockedUntil.Value > nowUtc)
        {
            return ShareLinkStatusNames.CoolingDown;
        }

        return ShareLinkStatusNames.Active;
    }

    /// <summary>
    /// Whether a link's settings may still be changed.
    ///
    /// <para>
    /// The rule turns on a distinction worth stating out loud: <b>expiry is time
    /// passing, revocation is a decision.</b> An expired window may be extended —
    /// the recipient still holds the link, and extending is the same act as
    /// sending them a fresh one without the bother of a new code. A revoked link
    /// may not be touched, because revoking is what somebody does when a link has
    /// reached the wrong person, and an edit that quietly reopened it would make
    /// "revoke" a promise this API does not keep.
    /// </para>
    ///
    /// <para>
    /// Named and separated from the caller so the rule is stated once and can be
    /// asserted without a database, rather than living as an <c>if</c> halfway
    /// down an update method.
    /// </para>
    /// </summary>
    /// <param name="revokedAt">When the link was withdrawn, or null while it stands.</param>
    /// <returns>True when the link may be edited.</returns>
    public static bool IsEditable(DateTime? revokedAt)
    {
        return !revokedAt.HasValue;
    }
}
