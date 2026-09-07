using CarPosAPI.Data;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Positions;

/// <summary>
/// Implements <see cref="IPositionErasureService"/> with a single bulk delete.
///
/// <c>ExecuteDeleteAsync</c> rather than loading and removing: the whole point is
/// that the range may be enormous, and pulling a year of fixes through the change
/// tracker to delete them would take the API down in the name of privacy.
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class PositionErasureService : IPositionErasureService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ILogger<PositionErasureService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    /// <param name="logger">Structured logger — device id and a row count, never coordinates.</param>
    public PositionErasureService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        ILogger<PositionErasureService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<long>> EraseAsync(
        int userId,
        string deviceId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<long>.NotFound("No such device.");
        }

        // Reading a history and destroying one are different acts, so this asks for
        // CanDelete rather than settling for the CanRead every active grant carries.
        // 403 rather than 404 here is deliberate and safe: the caller has already
        // proved they can see the device, so there is nothing left to conceal.
        if (!access.Permissions.CanDelete)
        {
            return OperationResult<long>.Forbidden(
                "You do not have permission to delete this device's data.");
        }

        IQueryable<Data.Entities.Position> query = _context.Positions
            .Where(position => position.DeviceId == access.DeviceRowId);

        if (fromUtc.HasValue)
        {
            DateTime from = NormaliseToUtc(fromUtc.Value);
            query = query.Where(position => position.FixTime >= from);
        }

        if (toUtc.HasValue)
        {
            DateTime to = NormaliseToUtc(toUtc.Value);
            query = query.Where(position => position.FixTime <= to);
        }

        long deleted = await query.ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} erased {DeletedCount} position(s) for device {DeviceId}",
            userId,
            deleted,
            access.DeviceId);

        return OperationResult<long>.Success(deleted);
    }

    /// <summary>
    /// Forces a query-string bound to <see cref="DateTimeKind.Utc"/>, exactly as
    /// <see cref="PositionQueryService"/> does — <c>fix_time</c> is
    /// <c>timestamptz</c>, and Npgsql refuses a parameter for such a column whose
    /// kind is anything else.
    /// </summary>
    /// <param name="value">A bound as model-bound from the query string.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>.</returns>
    private static DateTime NormaliseToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
