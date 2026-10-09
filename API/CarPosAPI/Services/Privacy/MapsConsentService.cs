using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Privacy;

/// <summary>
/// Implements <see cref="IMapsConsentService"/> over the two
/// <c>maps_consent_*</c> columns on <see cref="User"/>.
///
/// The server does not judge the version it is handed beyond its length: the
/// prompt text lives in the dashboard, so the dashboard decides whether a stored
/// answer still matches what it shows. What the server guarantees is that the
/// moment of agreement is its own clock, not the browser's.
///
/// Scoped — it holds a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class MapsConsentService : IMapsConsentService
{
    private readonly CarPosDbContext _context;
    private readonly ILogger<MapsConsentService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="logger">Structured logger — user ids only.</param>
    public MapsConsentService(CarPosDbContext context, ILogger<MapsConsentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<MapsConsentDto>> GetAsync(int userId, CancellationToken cancellationToken)
    {
        MapsConsentDto? consent = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new MapsConsentDto(user.MapsConsentVersion, user.MapsConsentGrantedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return consent is null
            ? OperationResult<MapsConsentDto>.NotFound(ErrorCodes.NoSuchUser, "No such user.")
            : OperationResult<MapsConsentDto>.Success(consent);
    }

    /// <inheritdoc />
    public async Task<OperationResult<MapsConsentDto>> GrantAsync(
        int userId,
        string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        User? user = await _context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<MapsConsentDto>.NotFound(ErrorCodes.NoSuchUser, "No such user.");
        }

        user.MapsConsentVersion = version.Trim();
        user.MapsConsentGrantedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Maps consent granted for user {UserId}", userId);

        return OperationResult<MapsConsentDto>.Success(
            new MapsConsentDto(user.MapsConsentVersion, user.MapsConsentGrantedAt));
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RevokeAsync(int userId, CancellationToken cancellationToken)
    {
        User? user = await _context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<bool>.NotFound(ErrorCodes.NoSuchUser, "No such user.");
        }

        // Cleared rather than flagged: once withdrawn, nothing relies on the old
        // agreement, so keeping when it was given would be data held for no purpose.
        user.MapsConsentVersion = null;
        user.MapsConsentGrantedAt = null;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Maps consent withdrawn for user {UserId}", userId);

        return OperationResult<bool>.Success(true);
    }
}
