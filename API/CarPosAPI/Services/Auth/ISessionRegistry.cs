using System.Security.Claims;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Makes stateless session tokens revocable. Every validated session token is
/// checked here (<c>OnTokenValidated</c> in <c>Program.cs</c>): it must carry the
/// user's current security stamp, and its id must not have been signed out.
/// </summary>
public interface ISessionRegistry
{
    /// <summary>Whether a validated token is still a live session.</summary>
    /// <param name="principal">The principal built from the token.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>False for a signed-out token, a superseded stamp, a deleted account or missing claims.</returns>
    Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    /// <summary>Ends the session a principal came from (sign-out). A no-op without one.</summary>
    /// <param name="principal">The caller.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the session is revoked.</returns>
    Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets the cached stamp of a user whose stamp just changed, so their other
    /// sessions end on their very next request rather than when the cache expires.
    /// </summary>
    /// <param name="userId">The account.</param>
    void ForgetUser(int userId);
}
