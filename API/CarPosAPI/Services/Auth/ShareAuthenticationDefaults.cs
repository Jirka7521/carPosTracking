namespace CarPosAPI.Services.Auth;

/// <summary>
/// Names belonging to the share-token authentication scheme.
///
/// <para>
/// The scheme is registered <b>alongside</b> the default JWT bearer one, never as
/// a replacement for it, and that arrangement is the wall between an anonymous
/// visitor and an account holder. A plain <c>[Authorize]</c> binds to the default
/// scheme, so every existing endpoint keeps rejecting share tokens without being
/// touched; the share endpoints name this scheme explicitly and reject session
/// tokens in the same way. Neither direction depends on anyone remembering to add
/// a check.
/// </para>
/// </summary>
public static class ShareAuthenticationDefaults
{
    /// <summary>
    /// Scheme name, used by <c>[Authorize(AuthenticationSchemes = ...)]</c> on the
    /// share endpoints and by the handler registration in <c>Program.cs</c>.
    /// </summary>
    public const string Scheme = "ShareToken";

    /// <summary>
    /// The claim carrying the share link's id.
    ///
    /// Note what is <em>not</em> in a share token: there is no <c>sub</c>, so
    /// <see cref="CurrentUserAccessor"/> can never resolve a user from one. Even if
    /// a share token somehow reached an account endpoint, there would be no identity
    /// in it to act as.
    /// </summary>
    public const string ShareClaim = "share";
}
