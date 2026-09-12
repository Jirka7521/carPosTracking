namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Decides how long a share link refuses codes after a wrong one.
///
/// <para>
/// <b>Why a cooldown rather than a permanent lock.</b> The recipient of a share is
/// usually somebody reading a code off a phone screen, and they will get it wrong.
/// A link that dies on the tenth mistake turns every fumble into a support request
/// to a person who may be driving. A delay that grows instead makes guessing
/// uneconomic while leaving an honest mistake self-healing — the cost falls on
/// whoever keeps being wrong, which is exactly the right place for it.
/// </para>
///
/// <para>
/// <b>What it is worth.</b> Once the ladder saturates, an attacker gets one guess
/// an hour against a code of roughly 59 bits. They also need the link's 256-bit
/// verifier first, without which no attempt reaches this code at all. The ladder is
/// therefore not the barrier — it is what stops the barrier from being worn down,
/// and what makes the attempt visible to the creator through
/// <c>FailedAttempts</c> while it happens.
/// </para>
///
/// Pure arithmetic over a count: no clock of its own, no state, no database. The
/// caller supplies "now", which is what makes every rung assertable in a test.
/// </summary>
internal static class ShareCooldownPolicy
{
    /// <summary>
    /// Wrong attempts that cost nothing but the attempt. Four is room to mistype a
    /// twelve-character code twice and still not be treated as an attacker.
    /// </summary>
    private const int FreeAttempts = 4;

    /// <summary>
    /// The ladder, in minutes, applied from the <see cref="FreeAttempts"/>+1'th
    /// failure onwards. The last entry holds for every failure beyond it — an hour
    /// is the ceiling because a longer one starts to resemble the permanent lock
    /// this policy exists to avoid.
    /// </summary>
    private static readonly int[] LadderMinutes = [1, 5, 15, 60];

    /// <summary>
    /// Works out when a link may next accept a code.
    /// </summary>
    /// <param name="failedAttempts">
    /// Consecutive failures <em>including</em> the one just recorded.
    /// </param>
    /// <param name="nowUtc">The current instant, supplied by the caller.</param>
    /// <returns>
    /// The instant the link unlocks, or null when this failure earns no delay.
    /// </returns>
    public static DateTime? NextUnlockUtc(int failedAttempts, DateTime nowUtc)
    {
        if (failedAttempts <= FreeAttempts)
        {
            return null;
        }

        // Index into the ladder, holding at the last rung rather than running off
        // the end. Math.Min is what makes "8th and every later failure" a single
        // case instead of a special one.
        int rung = Math.Min(failedAttempts - FreeAttempts - 1, LadderMinutes.Length - 1);

        return nowUtc.AddMinutes(LadderMinutes[rung]);
    }
}
