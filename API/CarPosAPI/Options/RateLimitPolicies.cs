namespace CarPosAPI.Options;

/// <summary>
/// Names of the rate-limiting policies configured in <c>Program.cs</c>. Constants
/// rather than literals so a policy referenced by an attribute can never drift
/// from the one that was registered — a typo there fails at runtime with a
/// confusing 500, not at compile time.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Applied to <c>/api/auth</c>. Sign-in is the one endpoint where an attacker
    /// gets unlimited free attempts at guessing, so it is capped per client
    /// address; everything else is protected by needing a valid session first.
    /// </summary>
    public const string Authentication = "auth";

    /// <summary>
    /// Applied to the two GDPR endpoints on <c>/api/me</c>. Needing a session is not
    /// enough of a limit for either: the export streams an entire, uncapped position
    /// history on every call, so repeating it is a cheap way to pin the database from
    /// one account, and erasure takes the current password, which makes it an online
    /// guessing surface that the sign-in limiter never sees.
    /// <para>
    /// Partitioned by account rather than by address, because both are authenticated:
    /// the account is the thing being abused, and one user behind a shared address
    /// must not be able to lock out another.
    /// </para>
    /// </summary>
    public const string PrivacyOperations = "privacy";
}
