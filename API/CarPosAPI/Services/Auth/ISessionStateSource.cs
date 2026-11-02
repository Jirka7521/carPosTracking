namespace CarPosAPI.Services.Auth;

/// <summary>
/// The stored facts <see cref="ISessionRegistry"/> decides with: a user's current
/// security stamp, and whether a token id has been signed out. Separate from the
/// registry so its caching and decisions are testable without a database.
/// </summary>
public interface ISessionStateSource
{
    /// <summary>Reads a user's current security stamp.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The stamp, or null when the account no longer exists.</returns>
    Task<string?> GetSecurityStampAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Whether a token id is on the deny-list.</summary>
    /// <param name="tokenId">The token's <c>jti</c>.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>True when that token was signed out.</returns>
    Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a token id on the deny-list until it would have expired, and prunes the
    /// entries whose tokens have expired since — the table only ever holds tokens
    /// that would otherwise still work.
    /// </summary>
    /// <param name="tokenId">The token's <c>jti</c>.</param>
    /// <param name="expiresAtUtc">When the token expires.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>A task that completes when the entry is stored.</returns>
    Task RevokeAsync(string tokenId, DateTime expiresAtUtc, CancellationToken cancellationToken);
}
