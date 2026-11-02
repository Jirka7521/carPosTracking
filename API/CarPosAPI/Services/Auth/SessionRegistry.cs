using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// Implements <see cref="ISessionRegistry"/>: two checks per request, each
/// answered from a short in-memory cache in front of <see cref="ISessionStateSource"/>.
///
/// <para>
/// <b>Why two mechanisms.</b> A per-user security stamp ends <em>every</em> session
/// of an account at once — what a password change has to do, since the reason for
/// changing it is often that someone else has it. A deny-list of token ids ends
/// <em>one</em> session — what "Sign out" means; the same user's other browsers
/// stay signed in. A stamp alone would sign out every device on every logout, and a
/// deny-list alone would need a row per live session to end them all.
/// </para>
///
/// <para>
/// <b>Why a cache, and why it is safe.</b> Without one every request costs two extra
/// queries. Entries live for <see cref="CacheLifetime"/>; this process is the only
/// writer (one API instance — see MqttOptions.ClientId), and it updates or evicts
/// the entry in the same breath as it writes, so a sign-out or a password change
/// takes effect on the very next request, not after the cache expires.
/// </para>
///
/// Singleton: the cache is process-wide by design.
/// </summary>
internal sealed class SessionRegistry : ISessionRegistry
{
    /// <summary>
    /// How long a stamp or a "not revoked" answer is reused. Thirty seconds turns a
    /// page's burst of API calls into one lookup, and is the most a change made
    /// outside this process (an operator editing the table by hand) can lag.
    /// </summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;
    private readonly ISessionStateSource _source;

    /// <summary>Creates the registry.</summary>
    /// <param name="cache">The process-wide memory cache.</param>
    /// <param name="source">Where stamps and revocations are stored.</param>
    public SessionRegistry(IMemoryCache cache, ISessionStateSource source)
    {
        _cache = cache;
        _source = source;
    }

    /// <inheritdoc />
    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(principal, out SessionIdentity session))
        {
            return false;
        }

        string? stamp = await _cache.GetOrCreateAsync(
            StampKey(session.UserId),
            (ICacheEntry entry) =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                return _source.GetSecurityStampAsync(session.UserId, cancellationToken);
            });

        if (stamp is null || !StampsMatch(stamp, session.SecurityStamp))
        {
            return false;
        }

        bool revoked = await _cache.GetOrCreateAsync(
            RevokedKey(session.TokenId),
            (ICacheEntry entry) =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
                return _source.IsRevokedAsync(session.TokenId, cancellationToken);
            });

        return !revoked;
    }

    /// <inheritdoc />
    public async Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!SessionClaims.TryRead(principal, out SessionIdentity session))
        {
            return;
        }

        await _source.RevokeAsync(session.TokenId, session.ExpiresAtUtc, cancellationToken);

        // Remembered as revoked until the token would have expired, so this process
        // never asks the database about it again.
        _cache.Set(RevokedKey(session.TokenId), true, new DateTimeOffset(session.ExpiresAtUtc, TimeSpan.Zero));
    }

    /// <inheritdoc />
    public void ForgetUser(int userId)
    {
        _cache.Remove(StampKey(userId));
    }

    /// <summary>Compares two stamps without leaking where they differ.</summary>
    /// <param name="stored">The user's current stamp.</param>
    /// <param name="presented">The token's.</param>
    /// <returns>True when equal.</returns>
    private static bool StampsMatch(string stored, string presented)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored),
            Encoding.UTF8.GetBytes(presented));
    }

    /// <summary>The cache key for a user's stamp.</summary>
    /// <param name="userId">The account.</param>
    /// <returns>The key.</returns>
    private static string StampKey(int userId)
    {
        return "session-stamp:" + userId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The cache key for a token id's revocation state.</summary>
    /// <param name="tokenId">The token's <c>jti</c>.</param>
    /// <returns>The key.</returns>
    private static string RevokedKey(string tokenId)
    {
        return "session-revoked:" + tokenId;
    }
}
