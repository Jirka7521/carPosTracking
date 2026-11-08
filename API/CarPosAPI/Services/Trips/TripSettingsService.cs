using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ITripSettingsService"/>.
///
/// <para>
/// Any grant may read the thresholds — a Normal-mode user can see "split at stops of
/// 20 min" — and <c>CanModifySettings</c> changes them. They live only on the server: the
/// tracker never sees them, so this is not a device settings revision.
/// </para>
///
/// <para>
/// <b>A change recomputes everything.</b> The save marks the device dirty from the start
/// of its history and bumps the epoch, in the same transaction — taking the trip worker's
/// lock row first, so a pass already running with the old thresholds throws its result
/// away — and then wakes the worker. Saving unchanged values does nothing.
/// </para>
///
/// Scoped.
/// </summary>
internal sealed class TripSettingsService : ITripSettingsService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ITripDetectionSignal _tripSignal;
    private readonly ILogger<TripSettingsService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    /// <param name="tripSignal">Wakes the trip worker after a change.</param>
    /// <param name="logger">Structured logger.</param>
    public TripSettingsService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        ITripDetectionSignal tripSignal,
        ILogger<TripSettingsService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _tripSignal = tripSignal;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<TripSettingsDto>> GetAsync(int userId, string deviceId, CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<TripSettingsDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        DeviceTripSettings? row = await _context.DeviceTripSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.DeviceId == access.DeviceRowId, cancellationToken);

        return OperationResult<TripSettingsDto>.Success(ToDto(row));
    }

    /// <inheritdoc />
    public async Task<OperationResult<TripSettingsDto>> UpdateAsync(
        int userId,
        string deviceId,
        TripSettingsUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<TripSettingsDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<TripSettingsDto>.Forbidden(
                ErrorCodes.NoPermissionManageTrips,
                "You do not have permission to change how this device's trips are detected.");
        }

        Guid deviceRowId = access.DeviceRowId;
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        (DeviceTripSettings? Row, bool Changed) saved = await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(attemptToken);

                DeviceTripSettings? row = await _context.DeviceTripSettings
                    .SingleOrDefaultAsync(settings => settings.DeviceId == deviceRowId, attemptToken);
                TripDetectionSettings current = TripDetectionSettings.From(row);
                TripDetectionSettings wanted = new TripDetectionSettings(
                    request.MinStopSeconds,
                    request.StopRadiusMeters,
                    request.MinTripDistanceMeters);

                // Unchanged — including "the defaults" on a device without a row — is a no-op:
                // no row written, nothing recomputed.
                if (current == wanted)
                {
                    return (row, false);
                }

                // The worker's lock row first (lock order: state row, then the rest), with
                // the epoch bump that makes an in-flight pass discard old-threshold results.
                await TripDirtyMarker.MarkAsync(_context, deviceRowId, DateTime.UnixEpoch, invalidateInFlight: true, attemptToken);

                if (row is null)
                {
                    row = new DeviceTripSettings { DeviceId = deviceRowId };
                    _context.DeviceTripSettings.Add(row);
                }

                row.MinStopSeconds = wanted.MinStopSeconds;
                row.StopRadiusMeters = wanted.StopRadiusMeters;
                row.MinTripDistanceMeters = wanted.MinTripDistanceMeters;
                row.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(attemptToken);
                await transaction.CommitAsync(attemptToken);
                return ((DeviceTripSettings?)row, true);
            },
            cancellationToken);

        if (saved.Changed)
        {
            _tripSignal.Notify();
            _logger.LogInformation(
                "User {UserId} changed trip detection on device {DeviceId}; recomputing its trips",
                userId,
                access.DeviceId);
        }

        return OperationResult<TripSettingsDto>.Success(ToDto(saved.Row));
    }

    private static TripSettingsDto ToDto(DeviceTripSettings? row)
    {
        TripDetectionSettings settings = TripDetectionSettings.From(row);
        return new TripSettingsDto(settings.MinStopSeconds, settings.StopRadiusMeters, settings.MinTripDistanceMeters, row is null);
    }
}
