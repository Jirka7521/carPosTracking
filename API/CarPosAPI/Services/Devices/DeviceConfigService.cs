using System.Linq.Expressions;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Ingest;
using CarPosAPI.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

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
/// the device's pointer. That work moved to <see cref="IDeviceConfigRevisionWriter"/>
/// when schedules gave it a second, user-less caller — this service is now the
/// permission check, the override rule, and the read model around it.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class DeviceConfigService : IDeviceConfigService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly IConfigPublisher _publisher;
    private readonly IDeviceConfigRevisionWriter _revisionWriter;
    private readonly IDeviceScheduleResolver _scheduleResolver;
    private readonly ILogger<DeviceConfigService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on a device.</param>
    /// <param name="publisher">Pushes a saved revision to the broker, retained.</param>
    /// <param name="revisionWriter">Appends revisions and publishes them; shares this context.</param>
    /// <param name="scheduleResolver">Works out when a schedule next switches, for overrides.</param>
    /// <param name="logger">Structured logger.</param>
    public DeviceConfigService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        IConfigPublisher publisher,
        IDeviceConfigRevisionWriter revisionWriter,
        IDeviceScheduleResolver scheduleResolver,
        ILogger<DeviceConfigService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _publisher = publisher;
        _revisionWriter = revisionWriter;
        _scheduleResolver = scheduleResolver;
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

        // Tracked, because the override stamped below rides to the database inside the
        // revision writer's transaction — the two must land together or not at all.
        Device? device = await _context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == access.DeviceRowId, cancellationToken);
        if (device is null)
        {
            return OperationResult<DeviceConfigStateDto>.NotFound("No such device.");
        }

        if (device.ConfigScheduleEnabled)
        {
            OperationResult<DeviceConfigStateDto>? refusal =
                await StampOverrideAsync(device, request, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }
        }

        ConfigRevisionOutcome? outcome = await _revisionWriter.ApplyAsync(
            device.Id,
            ToValues(request),
            userId,
            ConfigRevisionSource.Manual,
            sourceProfileId: null,
            cancellationToken);

        if (outcome is null)
        {
            return OperationResult<DeviceConfigStateDto>.NotFound("No such device.");
        }

        if (outcome.Changed)
        {
            _logger.LogInformation(
                "User {UserId} saved settings revision {Version} for device {DeviceId}",
                userId,
                outcome.Version,
                deviceId);
        }

        return await BuildStateAsync(device.Id, cancellationToken);
    }

    /// <summary>
    /// Applies the override rule to a manual save on a device whose schedule is on:
    /// checks the caller knew the save is temporary, and stamps the instant it lapses.
    ///
    /// <para>
    /// The device is only mutated in memory. Nothing is committed here — the writer's
    /// transaction picks the change up from the shared change tracker, so a device can
    /// never end up with the new revision but no override, or the reverse.
    /// </para>
    /// </summary>
    /// <param name="device">The tracked device row.</param>
    /// <param name="request">The submitted settings, carrying the acknowledgement.</param>
    /// <param name="cancellationToken">Cancels the rule lookup.</param>
    /// <returns>A failure to return to the caller, or null to carry on with the save.</returns>
    private async Task<OperationResult<DeviceConfigStateDto>?> StampOverrideAsync(
        Device device,
        UpdateDeviceConfigRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!request.AcknowledgeOverride)
        {
            return OperationResult<DeviceConfigStateDto>.Invalid(
                "This device is on a schedule, so saving settings by hand only holds until "
                + "the next scheduled switch. Confirm that you understand this, edit the "
                + "profile the schedule uses, or turn the schedule off.");
        }

        ScheduleEvaluation evaluation = await _scheduleResolver.ResolveAsync(
            device.Id,
            device.ConfigScheduleFallbackProfileId,
            DateTime.UtcNow,
            cancellationToken);

        if (evaluation.NextChangeAt is null)
        {
            // A schedule whose rules resolve the same way all week never switches, so
            // "temporary until the next switch" has nothing to expire at. Rather than
            // invent a horizon — a day? a week? — say so: the only honest way to change
            // such a device's settings is to change what the schedule itself says.
            return OperationResult<DeviceConfigStateDto>.Invalid(
                "This device's schedule never switches profiles, so a temporary change has "
                + "nothing to expire at. Edit the profile it uses, or turn the schedule off.");
        }

        device.ConfigOverrideUntil = evaluation.NextChangeAt;
        return null;
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
                .FirstOrDefault(),
            // A ternary over literals rather than a call to a mapping method: only a
            // constant survives translation into SQL, which is why the wire spellings
            // are consts in the first place — see ConfigRevisionSourceNames.
            configVersion.Source == ConfigRevisionSource.Schedule
                ? ConfigRevisionSourceNames.Schedule
                : ConfigRevisionSourceNames.Manual,
            // Another correlated subquery, and null-tolerant by construction: a profile
            // deleted since keeps the revision intact and simply loses its label.
            _context.DeviceConfigProfiles
                .Where(profile => profile.Id == configVersion.SourceProfileId)
                .Select(profile => profile.Name)
                .FirstOrDefault());
    }

    /// <summary>Strips the request down to the values the revision writer takes.</summary>
    /// <param name="request">The submitted settings.</param>
    /// <returns>The same seven values, without the acknowledgement flag.</returns>
    private static DeviceConfigValuesDto ToValues(UpdateDeviceConfigRequestDto request)
    {
        return new DeviceConfigValuesDto(
            request.IntervalSeconds,
            request.SleepBetween,
            request.FixTimeoutSeconds,
            request.QueueMaxFixes,
            request.RetryIntervalHours,
            request.RetryMaxAgeHours,
            request.ConfigCheckSeconds);
    }
}
