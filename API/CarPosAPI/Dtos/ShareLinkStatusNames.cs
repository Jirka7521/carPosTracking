namespace CarPosAPI.Dtos;

/// <summary>
/// The wire spellings of a share link's lifecycle state, as shown in the
/// creator's management list.
///
/// <para>
/// The state is <em>derived</em> — there is no status column, because every one of
/// these is a reading of the timestamps and counters that already exist, and a
/// stored copy would be one more thing that can disagree with them. It is computed
/// server-side rather than in the browser so that "is this link live" has exactly
/// one answer, produced by the same clock that enforces it.
/// </para>
/// </summary>
public static class ShareLinkStatusNames
{
    /// <summary>Withdrawn by its creator. Terminal.</summary>
    public const string Revoked = "revoked";

    /// <summary>Its window has passed. Terminal, and reached without anyone acting.</summary>
    public const string Expired = "expired";

    /// <summary>Created ahead of time; its window has not opened yet.</summary>
    public const string Scheduled = "scheduled";

    /// <summary>In its window, but refusing codes after repeated wrong attempts.</summary>
    public const string CoolingDown = "coolingDown";

    /// <summary>In its window and accepting the code.</summary>
    public const string Active = "active";
}
