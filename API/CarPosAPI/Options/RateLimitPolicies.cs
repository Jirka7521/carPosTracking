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
}
