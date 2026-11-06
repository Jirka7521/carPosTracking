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

    /// <summary>
    /// Applied to <c>/api/shares</c>'s anonymous actions. Redeeming a link is the
    /// second place in this API where an attacker gets free attempts, and unlike
    /// sign-in there is no account behind it to lock or notify.
    /// <para>
    /// It is the outer of two limits and they guard different things. The per-link
    /// cooldown makes repeated guessing against <em>one</em> share progressively
    /// useless; this one caps how fast a single address can work through
    /// <em>many</em>, which is the shape an attempt to find valid links would take.
    /// Partitioned by address, like sign-in, because a visitor has no identity here
    /// to partition by.
    /// </para>
    /// </summary>
    public const string ShareRedemption = "share";

    /// <summary>
    /// Applied to <c>/api/access/invitations</c>. Opening, accepting or declining an
    /// invitation takes a link token, so each call is one guess at a token; creating
    /// one puts another credential into circulation. The token's 256-bit verifier
    /// makes guessing hopeless anyway — this caps what one account can do in a loop,
    /// and partitions by account like <see cref="PrivacyOperations"/>, because every
    /// action here needs a session.
    /// </summary>
    public const string Invitations = "invitations";

    /// <summary>
    /// Applied to <c>POST /api/devices</c>. Each registration generates an RSA-3072
    /// key pair, seconds of CPU on the Pi, so a loop of them from one account is a
    /// denial of service. Partitioned by account; the per-account device cap
    /// (<c>DeviceRegistrationOptions</c>) is the second half of the same defence.
    /// </summary>
    public const string DeviceRegistration = "device-registration";
}
