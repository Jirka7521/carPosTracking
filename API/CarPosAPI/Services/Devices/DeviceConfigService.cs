using System.Linq.Expressions;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Implements <see cref="IDeviceConfigService"/>.
///
/// <para>
/// Like <see cref="DeviceService"/>, every method resolves the caller's grant first and
/// returns before any query is shaped by user input. Settings are gated on
/// <c>CanModifySettings</c> even for reads: the panel exposes the fleet's operational
/// tuning, which a read-only viewer has no use for — the same reasoning as
/// <see cref="DeviceService.GetProvisioningAsync"/>.
/// </para>
///
/// <para>
/// <b>The write is insert-only.</b> Nothing here ever updates a
/// <see cref="DeviceConfigVersion"/> row; a change appends the next revision and moves
/// the device's pointer. That is what preserves the values a device may still be
/// running while a newer revision waits to be picked up.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class DeviceConfigService : IDeviceConfigService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly IConfigPublisher _publisher;
    private readonly ILogger<DeviceConfigService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on a device.</param>
    /// <param name="publisher">Pushes a saved revision to the broker, retained.</param>
    /// <param name="logger">Structured logger.</param>
    public DeviceConfigService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        IConfigPublisher publisher,
        ILogger<DeviceConfigService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceConfigStateDto>> GetStateAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<DeviceConfigStateDto>.NotFound("No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<DeviceConfigStateDto>.Forbidden(
                "You do not have permission to view this device's settings.");
        }

        return await BuildStateAsync(access.DeviceRowId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<DeviceConfigVersionDto>>> GetHistoryAsync(
        int userId,
        string deviceId,
        int limit,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<IReadOnlyList<DeviceConfigVersionDto>>.NotFound("No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<IReadOnlyList<DeviceConfigVersionDto>>.Forbidden(
                "You do not have permission to view this device's settings.");
        }

        // Ordering and the limit are both in SQL. A device that has been retuned every
        // day for years must not pull its whole history into memory to show ten rows.
        List<DeviceConfigVersionDto> history = await _context.DeviceConfigVersions
            .AsNoTracking()
            .Where(configVersion => configVersion.DeviceId == access.DeviceRowId)
            .OrderByDescending(configVersion => configVersion.Version)
            .Take(limit)
            .Select(VersionProjection())
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<DeviceConfigVersionDto>>.Success(history);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceConfigStateDto>> UpdateAsync(
        int userId,
        string deviceId,
        UpdateDeviceConfigRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<DeviceConfigStateDto>.NotFound("No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<DeviceConfigStateDto>.Forbidden(
                "You do not have permission to change this device's settings.");
        }

        if (!access.IsActive)
        {
            // A retired device's ingest is rejected anyway, so publishing settings to it
            // would be theatre. Invalid rather than NotFound: the caller can see the
            // device, so pretending it does not exist would be needlessly confusing.
            return OperationResult<DeviceConfigStateDto>.Invalid(
                "This device has been deleted, so its settings can no longer be changed.");
        }

        Device? device = await _context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == access.DeviceRowId, cancellationToken);
        if (device is null)
        {
            return OperationResult<DeviceConfigStateDto>.NotFound("No such device.");
        }

        DeviceConfigVersion? current = await _context.DeviceConfigVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.DeviceId == device.Id && candidate.Version == device.ConfigVersion,
                cancellationToken);

        // Saving what is already in force must not append a revision — otherwise an
        // impatient double-click, or a UI that re-submits unchanged values, would fill
        // the history with rows that say nothing.
        if (current is not null && Matches(current, request))
        {
            _logger.LogDebug(
                "Device {DeviceId}: settings unchanged, keeping revision {Version}",
                deviceId,
                current.Version);
            return await BuildStateAsync(device.Id, cancellationToken);
        }

        // The new row and the pointer that makes it live are one unit of work: a
        // committed revision nobody points at is invisible, and a pointer to a row that
        // was rolled back would break every read.
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        // Derived from the pointer rather than MAX(version) so the numbering can never
        // step on a row that already exists — the unique index would refuse it anyway,
        // but this way the intent is explicit.
        int nextVersion = device.ConfigVersion + 1;

        _context.DeviceConfigVersions.Add(new DeviceConfigVersion
        {
            DeviceId = device.Id,
            Version = nextVersion,
            IntervalSeconds = request.IntervalSeconds,
            SleepBetween = request.SleepBetween,
            FixTimeoutSeconds = request.FixTimeoutSeconds,
            QueueMaxFixes = request.QueueMaxFixes,
            RetryIntervalHours = request.RetryIntervalHours,
            RetryMaxAgeHours = request.RetryMaxAgeHours,
            ConfigCheckSeconds = request.ConfigCheckSeconds,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
        });

        device.ConfigVersion = nextVersion;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} saved settings revision {Version} for device {DeviceId}",
            userId,
            nextVersion,
            deviceId);

        // Published after the commit, deliberately. If the broker is unreachable the
        // save still stands and the reconnect sweep delivers it; publishing first could
        // hand a device a revision that a failed commit then erased.
        await _publisher.PublishAsync(
            device.DeviceId,
            ToDocument(nextVersion, request),
            cancellationToken);

        return await BuildStateAsync(device.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RepublishAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<bool>.NotFound("No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<bool>.Forbidden(
                "You do not have permission to change this device's settings.");
        }

        DeviceConfigPublication? publication = await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == access.DeviceRowId)
            .Join(
                _context.DeviceConfigVersions.AsNoTracking(),
                device => new { DeviceRowId = device.Id, Version = device.ConfigVersion },
                configVersion => new { DeviceRowId = configVersion.DeviceId, configVersion.Version },
                (device, configVersion) => new DeviceConfigPublication(
                    device.DeviceId,
                    new DeviceConfigDocumentDto(
                        configVersion.Version,
                        configVersion.IntervalSeconds,
                        configVersion.SleepBetween,
                        configVersion.FixTimeoutSeconds,
                        configVersion.QueueMaxFixes,
                        configVersion.RetryIntervalHours,
                        configVersion.RetryMaxAgeHours,
                        configVersion.ConfigCheckSeconds)))
            .SingleOrDefaultAsync(cancellationToken);

        if (publication is null)
        {
            return OperationResult<bool>.NotFound("This device has no stored configuration to publish.");
        }

        bool published = await _publisher.PublishAsync(
            publication.DeviceId,
            publication.Document,
            cancellationToken);

        // A false return is reported as success with a false value, not as an error:
        // "the broker is not reachable right now" is operational information the UI
        // shows, not a failed request — the settings are unchanged either way.
        return OperationResult<bool>.Success(published);
    }

    /// <summary>
    /// Loads the desired and applied revisions for one device and assembles the state.
    /// </summary>
    /// <param name="deviceRowId">Internal device id, already authorised.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The state, or NotFound when the device has no configuration at all.</returns>
    private async Task<OperationResult<DeviceConfigStateDto>> BuildStateAsync(
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        DeviceConfigPointers? pointers = await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == deviceRowId)
            .Select(device => new DeviceConfigPointers(
                device.ConfigVersion,
                device.ConfigAppliedVersion,
                device.ConfigAppliedAt,
                device.LastSeenAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (pointers is null)
        {
            return OperationResult<DeviceConfigStateDto>.NotFound("No such device.");
        }

        // Both revisions in one round trip. They are usually the same row, and the
        // applied one may not exist at all (the device has never reported), so this is
        // an OR over at most two version numbers rather than two separate lookups.
        List<DeviceConfigVersionDto> revisions = await _context.DeviceConfigVersions
            .AsNoTracking()
            .Where(configVersion => configVersion.DeviceId == deviceRowId
                && (configVersion.Version == pointers.DesiredVersion
                    || configVersion.Version == pointers.AppliedVersion))
            .Select(VersionProjection())
            .ToListAsync(cancellationToken);

        DeviceConfigVersionDto? desired =
            revisions.SingleOrDefault(revision => revision.Version == pointers.DesiredVersion);

        if (desired is null)
        {
            // The pointer names a revision that is not there. Only reachable if the
            // rows were edited by hand, but answering with a half-built state would be
            // worse than saying so plainly.
            _logger.LogError(
                "Device row {DeviceRowId} points at config version {Version}, which does not exist",
                deviceRowId,
                pointers.DesiredVersion);
            return OperationResult<DeviceConfigStateDto>.NotFound(
                "This device has no stored configuration.");
        }

        DeviceConfigVersionDto? applied = pointers.AppliedVersion is null
            ? null
            : revisions.SingleOrDefault(revision => revision.Version == pointers.AppliedVersion);

        return OperationResult<DeviceConfigStateDto>.Success(new DeviceConfigStateDto(
            desired,
            applied,
            pointers.AppliedAt,
            pointers.AppliedVersion == pointers.DesiredVersion,
            pointers.LastSeenAt));
    }

    /// <summary>
    /// The projection from a revision row to its DTO, defined once and handed to both
    /// queries that need it.
    ///
    /// <para>
    /// It is an <see cref="Expression{TDelegate}"/> rather than a method so EF Core can
    /// translate it into SQL. The author's name comes from a correlated subquery — the
    /// same trick <see cref="DeviceService.ListForUserAsync"/> uses for aliases — which
    /// keeps a history of any length to a single round trip instead of one lookup per
    /// row.
    /// </para>
    /// </summary>
    /// <returns>An EF-translatable projection expression.</returns>
    private Expression<Func<DeviceConfigVersion, DeviceConfigVersionDto>> VersionProjection()
    {
        return configVersion => new DeviceConfigVersionDto(
            configVersion.Version,
            new DeviceConfigValuesDto(
                configVersion.IntervalSeconds,
                configVersion.SleepBetween,
                configVersion.FixTimeoutSeconds,
                configVersion.QueueMaxFixes,
                configVersion.RetryIntervalHours,
                configVersion.RetryMaxAgeHours,
                configVersion.ConfigCheckSeconds),
            configVersion.CreatedAt,
            _context.Users
                .Where(user => user.Id == configVersion.CreatedByUserId)
                .Select(user => user.FirstName + " " + user.LastName)
                .FirstOrDefault());
    }

    /// <summary>Builds the firmware-facing document for a revision.</summary>
    /// <param name="version">The revision number.</param>
    /// <param name="request">The settings it carries.</param>
    /// <returns>The document to publish retained.</returns>
    private static DeviceConfigDocumentDto ToDocument(int version, UpdateDeviceConfigRequestDto request)
    {
        return new DeviceConfigDocumentDto(
            version,
            request.IntervalSeconds,
            request.SleepBetween,
            request.FixTimeoutSeconds,
            request.QueueMaxFixes,
            request.RetryIntervalHours,
            request.RetryMaxAgeHours,
            request.ConfigCheckSeconds);
    }

    /// <summary>Whether a stored revision already carries exactly these values.</summary>
    /// <param name="stored">The revision currently in force.</param>
    /// <param name="request">The submitted settings.</param>
    /// <returns>True when saving would change nothing.</returns>
    private static bool Matches(DeviceConfigVersion stored, UpdateDeviceConfigRequestDto request)
    {
        return stored.IntervalSeconds == request.IntervalSeconds
            && stored.SleepBetween == request.SleepBetween
            && stored.FixTimeoutSeconds == request.FixTimeoutSeconds
            && stored.QueueMaxFixes == request.QueueMaxFixes
            && stored.RetryIntervalHours == request.RetryIntervalHours
            && stored.RetryMaxAgeHours == request.RetryMaxAgeHours
            && stored.ConfigCheckSeconds == request.ConfigCheckSeconds;
    }
}
