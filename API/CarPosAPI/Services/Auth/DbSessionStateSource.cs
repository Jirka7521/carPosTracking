using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// <see cref="ISessionStateSource"/> over PostgreSQL. A singleton, like the
/// registry that uses it, so each call opens its own short-lived context from the
/// factory rather than holding a scoped one.
/// </summary>
internal sealed class DbSessionStateSource : ISessionStateSource
{
    private readonly IDbContextFactory<CarPosDbContext> _contextFactory;

    /// <summary>Creates the source.</summary>
    /// <param name="contextFactory">Makes a context per call.</param>
    public DbSessionStateSource(IDbContextFactory<CarPosDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<string?> GetSecurityStampAsync(int userId, CancellationToken cancellationToken)
    {
        await using CarPosDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken)
    {
        await using CarPosDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.RevokedSessions
            .AsNoTracking()
            .AnyAsync(session => session.TokenId == tokenId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string tokenId, DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        await using CarPosDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        // Housekeeping on the write path, so the table needs no job of its own: a
        // row is only worth keeping while the token it names could still be used.
        await context.RevokedSessions
            .Where(session => session.ExpiresAt < nowUtc)
            .ExecuteDeleteAsync(cancellationToken);

        bool known = await context.RevokedSessions
            .AnyAsync(session => session.TokenId == tokenId, cancellationToken);

        if (known)
        {
            // A second sign-out with the same cookie (two tabs) is a no-op.
            return;
        }

        context.RevokedSessions.Add(new RevokedSession { TokenId = tokenId, ExpiresAt = expiresAtUtc });
        await context.SaveChangesAsync(cancellationToken);
    }
}
