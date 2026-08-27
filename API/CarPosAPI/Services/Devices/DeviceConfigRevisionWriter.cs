using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Implements <see cref="IDeviceConfigRevisionWriter"/>. This code lived inside
/// <c>DeviceConfigService.UpdateAsync</c> until schedules gave it a second caller; the
/// reasoning in the comments below is unchanged from there.
/// </summary>
internal sealed class DeviceConfigRevisionWriter : IDeviceConfigRevisionWriter
{
    private readonly CarPosDbContext _context;
    private readonly IConfigPublisher _publisher;
    private readonly ILogger<DeviceConfigRevisionWriter> _logger;

    /// <summary>Creates the writer.</summary>
    /// <param name="context">Scoped database context, shared with the caller.</param>
    /// <param name="publisher">Pushes a saved revision to the broker, retained.</param>
    /// <param name="logger">Structured logger.</param>
    public DeviceConfigRevisionWriter(
        CarPosDbContext context,
        IConfigPublisher publisher,
        ILogger<DeviceConfigRevisionWriter> logger)
    {
        _context = context;
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConfigRevisionOutcome?> ApplyAsync(
        Guid deviceRowId,
        DeviceConfigValuesDto values,
        int? authorUserId,
        ConfigRevisionSource source,
        Guid? sourceProfileId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);

        // Tracked, not AsNoTracking: the pointer below is written through this instance,
        // and any change the caller staged on the same row rides along in the commit.
        Device? device = await _context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == deviceRowId, cancellationToken);
        if (device is null)
        {
            return null;
        }

        DeviceConfigVersion? current = await _context.DeviceConfigVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.DeviceId == device.Id && candidate.Version == device.ConfigVersion,
                cancellationToken);

        // Saving what is already in force must not append a revision — otherwise an
        // impatient double-click, or a UI that re-submits unchanged values, would fill
        // the history with rows that say nothing. The scheduler leans on this far
        // harder than the dashboard ever did: it reconciles every thirty seconds, and
        // all but a handful of those passes find the device already correct.
        bool isUnchanged = current is not null && Matches(current, values);

        // The new row and the pointer that makes it live are one unit of work: a
        // committed revision nobody points at is invisible, and a pointer to a row that
        // was rolled back would break every read. The unchanged path still commits,
        // because the caller may have staged something of its own — an override stamp,
        // an evaluation timestamp — that must land whether or not the values moved.
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        // Derived from the pointer rather than MAX(version) so the numbering can never
        // step on a row that already exists — the unique index would refuse it anyway,
        // but this way the intent is explicit.
        int effectiveVersion = isUnchanged ? device.ConfigVersion : device.ConfigVersion + 1;

        if (!isUnchanged)
        {
            _context.DeviceConfigVersions.Add(new DeviceConfigVersion
            {
                DeviceId = device.Id,
                Version = effectiveVersion,
                IntervalSeconds = values.IntervalSeconds,
                SleepBetween = values.SleepBetween,
                FixTimeoutSeconds = values.FixTimeoutSeconds,
                QueueMaxFixes = values.QueueMaxFixes,
                RetryIntervalHours = values.RetryIntervalHours,
                RetryMaxAgeHours = values.RetryMaxAgeHours,
                ConfigCheckSeconds = values.ConfigCheckSeconds,
                CreatedByUserId = authorUserId,
                CreatedAt = DateTime.UtcNow,
                Source = source,
                SourceProfileId = sourceProfileId,
            });

            device.ConfigVersion = effectiveVersion;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (isUnchanged)
        {
            _logger.LogDebug(
                "Device {DeviceId}: settings unchanged, keeping revision {Version}",
                device.DeviceId,
                effectiveVersion);
            return new ConfigRevisionOutcome(effectiveVersion, false);
        }

        _logger.LogInformation(
            "Device {DeviceId}: settings revision {Version} saved from {Source}",
            device.DeviceId,
            effectiveVersion,
            source);

        // Published after the commit, deliberately. If the broker is unreachable the
        // save still stands and the reconnect sweep delivers it; publishing first could
        // hand a device a revision that a failed commit then erased.
        await _publisher.PublishAsync(
            device.DeviceId,
            ToDocument(effectiveVersion, values),
            cancellationToken);

        return new ConfigRevisionOutcome(effectiveVersion, true);
    }

    /// <summary>Builds the firmware-facing document for a revision.</summary>
    /// <param name="version">The revision number.</param>
    /// <param name="values">The settings it carries.</param>
    /// <returns>The document to publish retained.</returns>
    private static DeviceConfigDocumentDto ToDocument(int version, DeviceConfigValuesDto values)
    {
        return new DeviceConfigDocumentDto(
            version,
            values.IntervalSeconds,
            values.SleepBetween,
            values.FixTimeoutSeconds,
            values.QueueMaxFixes,
            values.RetryIntervalHours,
            values.RetryMaxAgeHours,
            values.ConfigCheckSeconds);
    }

    /// <summary>Whether a stored revision already carries exactly these values.</summary>
    /// <param name="stored">The revision currently in force.</param>
    /// <param name="values">The settings being applied.</param>
    /// <returns>True when writing would change nothing.</returns>
    private static bool Matches(DeviceConfigVersion stored, DeviceConfigValuesDto values)
    {
        return stored.IntervalSeconds == values.IntervalSeconds
            && stored.SleepBetween == values.SleepBetween
            && stored.FixTimeoutSeconds == values.FixTimeoutSeconds
            && stored.QueueMaxFixes == values.QueueMaxFixes
            && stored.RetryIntervalHours == values.RetryIntervalHours
            && stored.RetryMaxAgeHours == values.RetryMaxAgeHours
            && stored.ConfigCheckSeconds == values.ConfigCheckSeconds;
    }
}
