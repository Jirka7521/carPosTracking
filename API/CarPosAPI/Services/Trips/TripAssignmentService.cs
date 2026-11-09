using CarPosAPI.Data;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ITripAssignmentService"/> with single set-based updates.
///
/// <para>
/// <b>Rights.</b> Organising a car's trips changes what everyone with access to it sees,
/// so it asks for <c>CanModifySettings</c> — the same right that changes how the car is
/// tracked. Nothing is sent to the device, so unlike a settings save it is allowed on a
/// retired one.
/// </para>
///
/// <para>
/// <b>No device lock needed.</b> These statements touch trip rows only. The trip worker
/// locks the device's state row and then the same trip rows, so the two serialise on the
/// rows without any chance of a deadlock, and the worker's matcher reads the placement
/// these statements committed.
/// </para>
///
/// Scoped.
/// </summary>
internal sealed class TripAssignmentService : ITripAssignmentService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ILogger<TripAssignmentService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    /// <param name="logger">Structured logger — ids and counts only.</param>
    public TripAssignmentService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        ILogger<TripAssignmentService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RenameAsync(
        int userId,
        string deviceId,
        long tripId,
        string name,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<bool>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<bool>.Forbidden(
                ErrorCodes.NoPermissionManageTrips,
                "You do not have permission to organise this device's trips.");
        }

        // "No name" has one representation: NULL (the table's CHECK refuses a blank).
        string? trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        int updated = await _context.Trips
            .Where(trip => trip.Id == tripId && trip.DeviceId == access.DeviceRowId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(trip => trip.Name, trimmed), cancellationToken);

        return updated == 0
            ? OperationResult<bool>.NotFound(ErrorCodes.NoSuchTrip, "No such trip.")
            : OperationResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TripAssignResultDto>> AssignAsync(
        int userId,
        string deviceId,
        TripAssignRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<TripAssignResultDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<TripAssignResultDto>.Forbidden(
                ErrorCodes.NoPermissionManageTrips,
                "You do not have permission to organise this device's trips.");
        }

        Guid? journeyId = request.Automatic ? null : request.JourneyId;
        if (journeyId is Guid target
            && !await _context.Journeys.AnyAsync(
                journey => journey.Id == target && journey.DeviceId == access.DeviceRowId,
                cancellationToken))
        {
            return OperationResult<TripAssignResultDto>.NotFound(ErrorCodes.NoSuchJourney, "No such journey.");
        }

        List<long> tripIds = request.TripIds.Distinct().ToList();
        bool pinned = !request.Automatic;

        // Scoped to the device: an id belonging to another device is simply not matched.
        int moved = await _context.Trips
            .Where(trip => trip.DeviceId == access.DeviceRowId && tripIds.Contains(trip.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(trip => trip.JourneyId, journeyId)
                    .SetProperty(trip => trip.JourneyPinned, pinned),
                cancellationToken);

        _logger.LogInformation(
            "User {UserId} {Action} {MovedCount} trip(s) on device {DeviceId}",
            userId,
            request.Automatic ? "unpinned" : "pinned",
            moved,
            access.DeviceId);

        return OperationResult<TripAssignResultDto>.Success(new TripAssignResultDto(moved));
    }
}
