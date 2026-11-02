using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CarPosAPI.Data.Entities;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using Microsoft.Extensions.Caching.Memory;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins what makes a stateless session revocable: a token must carry the user's
/// current security stamp and must not have been signed out — and the cache in
/// front of both must never let a sign-out or a password change lag behind.
/// Uses an in-memory <see cref="ISessionStateSource"/>, so no database is needed.
/// </summary>
public sealed class SessionRegistryTests
{
    private const int UserId = 42;
    private const string Stamp = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task ATokenWithTheCurrentStampIsALiveSession()
    {
        (SessionRegistry registry, FakeSource _) = Create();

        Assert.True(await registry.IsCurrentAsync(Principal(Stamp, "jti-1"), CancellationToken.None));
    }

    [Fact]
    public async Task SigningOutEndsThatSessionAndNoOther()
    {
        (SessionRegistry registry, FakeSource source) = Create();

        // Cached as "not revoked" first, so the test also proves the cache is
        // updated rather than waited out.
        Assert.True(await registry.IsCurrentAsync(Principal(Stamp, "jti-1"), CancellationToken.None));

        await registry.RevokeAsync(Principal(Stamp, "jti-1"), CancellationToken.None);

        Assert.Contains("jti-1", source.Revoked);
        Assert.False(await registry.IsCurrentAsync(Principal(Stamp, "jti-1"), CancellationToken.None));
        Assert.True(await registry.IsCurrentAsync(Principal(Stamp, "jti-2"), CancellationToken.None));
    }

    [Fact]
    public async Task ANewStampEndsEveryEarlierSessionOnceForgotten()
    {
        (SessionRegistry registry, FakeSource source) = Create();
        Assert.True(await registry.IsCurrentAsync(Principal(Stamp, "jti-1"), CancellationToken.None));

        // What a password change does: new stamp stored, cached one forgotten.
        source.Stamps[UserId] = "ffffffffffffffffffffffffffffffff";
        registry.ForgetUser(UserId);

        Assert.False(await registry.IsCurrentAsync(Principal(Stamp, "jti-1"), CancellationToken.None));
        Assert.True(await registry.IsCurrentAsync(Principal("ffffffffffffffffffffffffffffffff", "jti-3"), CancellationToken.None));
    }

    [Fact]
    public async Task AnErasedAccountHasNoLiveSessions()
    {
        (SessionRegistry registry, FakeSource source) = Create();
        source.Stamps.Remove(UserId);

        Assert.False(await registry.IsCurrentAsync(Principal(Stamp, "jti-1"), CancellationToken.None));
    }

    [Fact]
    public async Task ATokenWithoutAStampIsRefused()
    {
        // Every token issued before this change: refused rather than trusted, so a
        // deployment signs everyone out once instead of leaving them unrevocable.
        (SessionRegistry registry, FakeSource _) = Create();

        ClaimsPrincipal legacy = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "42"),
            new Claim(JwtRegisteredClaimNames.Jti, "jti-1"),
            new Claim(JwtRegisteredClaimNames.Exp, "4102444800"),
        ], "test"));

        Assert.False(await registry.IsCurrentAsync(legacy, CancellationToken.None));
    }

    [Fact]
    public void IssuedTokensCarryTheStamp()
    {
        JwtTokenIssuer issuer = new JwtTokenIssuer(Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Issuer = "carpos-api",
            Audience = "carpos-fe",
            SigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256",
            LifetimeHours = 8,
        }));

        IssuedToken token = issuer.Issue(new User { Id = UserId, SecurityStamp = Stamp });

        JwtSecurityToken parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
        ClaimsPrincipal principal = new ClaimsPrincipal(new ClaimsIdentity(parsed.Claims, "test"));

        Assert.True(SessionClaims.TryRead(principal, out SessionIdentity session));
        Assert.Equal(UserId, session.UserId);
        Assert.Equal(Stamp, session.SecurityStamp);
        Assert.False(string.IsNullOrEmpty(session.TokenId));
        Assert.True(session.ExpiresAtUtc > DateTime.UtcNow);
    }

    /// <summary>A registry over a fresh cache and an in-memory source holding one user.</summary>
    /// <returns>The registry and its source.</returns>
    private static (SessionRegistry Registry, FakeSource Source) Create()
    {
        FakeSource source = new FakeSource();
        source.Stamps[UserId] = Stamp;

        return (new SessionRegistry(new MemoryCache(new MemoryCacheOptions()), source), source);
    }

    /// <summary>A principal as the JWT handler would build it from a session token.</summary>
    /// <param name="stamp">The token's stamp claim.</param>
    /// <param name="tokenId">Its jti.</param>
    /// <returns>The principal.</returns>
    private static ClaimsPrincipal Principal(string stamp, string tokenId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "42"),
            new Claim(SessionClaims.SecurityStamp, stamp),
            new Claim(JwtRegisteredClaimNames.Jti, tokenId),
            new Claim(JwtRegisteredClaimNames.Exp, "4102444800"),
        ], "test"));
    }

    /// <summary>An in-memory <see cref="ISessionStateSource"/>.</summary>
    private sealed class FakeSource : ISessionStateSource
    {
        public Dictionary<int, string> Stamps { get; } = new Dictionary<int, string>();

        public HashSet<string> Revoked { get; } = new HashSet<string>(StringComparer.Ordinal);

        public Task<string?> GetSecurityStampAsync(int userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Stamps.TryGetValue(userId, out string? stamp) ? stamp : null);
        }

        public Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Revoked.Contains(tokenId));
        }

        public Task RevokeAsync(string tokenId, DateTime expiresAtUtc, CancellationToken cancellationToken)
        {
            Revoked.Add(tokenId);
            return Task.CompletedTask;
        }
    }
}
