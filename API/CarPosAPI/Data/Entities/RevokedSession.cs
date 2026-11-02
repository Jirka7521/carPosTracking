namespace CarPosAPI.Data.Entities;

/// <summary>
/// A session token that was signed out before it expired. Sessions are stateless
/// JWTs, so without this a token copied out of a browser kept working for the rest
/// of its eight hours after the user pressed "Sign out".
///
/// <para>
/// A deny-list of token ids rather than a table of live sessions: it only grows on
/// sign-out, not on every sign-in, and a row is useless — and deleted — once the
/// token it names would have expired anyway. It holds no user id: the token id is
/// all the check needs, and the table then carries no personal data.
/// </para>
/// Mapped by <see cref="Configurations.RevokedSessionConfiguration"/>.
/// </summary>
public sealed class RevokedSession
{
    /// <summary>The token's <c>jti</c> claim. Primary key.</summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>When the token would have expired (UTC); the row is pruned after it.</summary>
    public DateTime ExpiresAt { get; set; }
}
