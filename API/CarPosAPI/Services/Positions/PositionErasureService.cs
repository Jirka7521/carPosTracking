using CarPosAPI.Data;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Trips;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Positions;

/// <summary>
/// Implements <see cref="IPositionErasureService"/> with bulk deletes in one
/// transaction — the positions, the connection history and the trips that started over
/// the same range, so an erasure can never leave one part of the record behind.
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
    private readonly ITripDetectionSignal _tripSignal;
    private readonly ILogger<PositionErasureService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    /// <param name="tripSignal">Wakes the trip worker to rebuild trips over the erased stretch.</param>
    /// <param name="logger">Structured logger — device id and row counts, never coordinates.</param>
    public PositionErasureService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        ITripDetectionSignal tripSignal,
        ILogger<PositionErasureService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _tripSignal = tripSignal;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<PositionErasureResultDto>> EraseAsync(
        int userId,
        string deviceId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<PositionErasureResultDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        // Reading a history and destroying one are different acts, so this asks for
        // CanDelete rather than settling for the CanRead every active grant carries.
        // 403 rather than 404 here is deliberate and safe: the caller has already
        // proved they can see the device, so there is nothing left to conceal.
        if (!access.Permissions.CanDelete)
        {
            return OperationResult<PositionErasureResultDto>.Forbidden(
                ErrorCodes.NoPermissionDeleteData,
                "You do not have permission to delete this device's data.");
        }

        DateTime? from = fromUtc.HasValue ? NormaliseToUtc(fromUtc.Value) : null;
        DateTime? to = toUtc.HasValue ? NormaliseToUtc(toUtc.Value) : null;

        // The connection retries transient faults (Program.cs), so the transaction has
        // to run inside the execution strategy, which may replay it whole.
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        (PositionErasureResultDto Dto, long TripsDeleted) erasure = await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                await using IDbContextTransaction transaction =
                    await _context.Database.BeginTransactionAsync(attemptToken);

                IQueryable<Data.Entities.Position> positions = _context.Positions
                    .Where(position => position.DeviceId == access.DeviceRowId);
                if (from.HasValue)
                {
                    positions = positions.Where(position => position.FixTime >= from.Value);
                }

                if (to.HasValue)
                {
                    positions = positions.Where(position => position.FixTime <= to.Value);
                }

                // Events have no fix time; their occurred time is the "when" - the one
                // the dashboard shows them by, so a range erases what the reader saw in it.
                IQueryable<Data.Entities.DeviceEvent> events = _context.DeviceEvents
                    .Where(deviceEvent => deviceEvent.DeviceId == access.DeviceRowId);
                if (from.HasValue)
                {
                    events = events.Where(deviceEvent => deviceEvent.OccurredAt >= from.Value);
                }

                if (to.HasValue)
                {
                    events = events.Where(deviceEvent => deviceEvent.OccurredAt <= to.Value);
                }

                // Trips first, under the trip worker's lock: the upsert takes the device's
                // trip_detection_state row and bumps its epoch, so a pass computing from
                // the fixes about to vanish throws its result away instead of writing it
                // back, and the erased stretch is marked for recomputation. Trips that
                // started inside the range lose their start fix with it, so they go; the
                // worker rebuilds whatever the remaining fixes still support.
                await TripDirtyMarker.MarkAsync(
                    _context,
                    access.DeviceRowId,
                    from ?? DateTime.UnixEpoch,
                    invalidateInFlight: true,
                    attemptToken);

                IQueryable<Data.Entities.Trip> trips = _context.Trips
                    .Where(trip => trip.DeviceId == access.DeviceRowId);
                if (from.HasValue)
                {
                    trips = trips.Where(trip => trip.StartTime >= from.Value);
                }

                if (to.HasValue)
                {
                    trips = trips.Where(trip => trip.StartTime <= to.Value);
                }

                long tripsDeleted = await trips.ExecuteDeleteAsync(attemptToken);
                long positionsDeleted = await positions.ExecuteDeleteAsync(attemptToken);
                long eventsDeleted = await events.ExecuteDeleteAsync(attemptToken);

                await transaction.CommitAsync(attemptToken);

                return (Dto: new PositionErasureResultDto(positionsDeleted, eventsDeleted), TripsDeleted: tripsDeleted);
            },
            cancellationToken);

        // After the commit, so the woken pass sees the erasure.
        _tripSignal.Notify();

        _logger.LogInformation(
            "User {UserId} erased {DeletedCount} position(s), {DeletedEventCount} event(s) and {DeletedTripCount} trip(s) for device {DeviceId}",
            userId,
            erasure.Dto.DeletedCount,
            erasure.Dto.DeletedEventCount,
            erasure.TripsDeleted,
            access.DeviceId);

        return OperationResult<PositionErasureResultDto>.Success(erasure.Dto);
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
