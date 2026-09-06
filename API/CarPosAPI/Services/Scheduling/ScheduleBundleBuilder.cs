using System.Globalization;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Ingest;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Turns a device's profiles, rules, fallback and override into the
/// <see cref="DeviceScheduleBundleDto"/> that goes onto <c>devices/&lt;id&gt;/schedule</c>.
///
/// <para>
/// <b>The database context arrives as a method parameter, not a constructor
/// dependency.</b> Two very differently-scoped callers need this: the scoped
/// <see cref="IScheduleBundlePublisher"/>, which must build inside the same unit of
/// work that just changed a rule, and the singleton <c>MqttConfigPublisher</c>, which
/// reaches the database through an <see cref="IDbContextFactory{TContext}"/> on the
/// reconnect sweep. Taking the context per call lets this stay a stateless singleton —
/// the same shape as <see cref="ScheduleEvaluator"/> — instead of forcing one of those
/// two callers into a lifetime that does not suit it.
/// </para>
///
/// <para>
/// The fleet-wide path is a handful of queries for the whole fleet, grouped in memory,
/// for the reason spelled out on <see cref="ScheduleReconciler"/>: it runs on every
/// broker reconnect, and the per-device shape is the one that gets slower the more
/// devices there are.
/// </para>
/// </summary>
internal sealed class ScheduleBundleBuilder
{
    /// <summary>
    /// The exact instant format the firmware accepts. <c>CivilTime::parseIso</c>
    /// insists on these twenty characters and rejects everything else — no fractional
    /// seconds, no offset, no lower-case z. Getting this wrong would not throw
    /// anywhere; the device would simply ignore every override it was ever sent.
    /// </summary>
    private const string DeviceInstantFormat = "yyyy-MM-ddTHH:mm:ss'Z'";

    /// <summary>Builds the bundle for one device.</summary>
    /// <param name="context">Database context to read through.</param>
    /// <param name="deviceRowId">The device's internal row id.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The bundle, or null when no such device exists.</returns>
    public async Task<DeviceScheduleBundleDto?> BuildAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ScheduleBundleDeviceSnapshot? device = await context.Devices
            .AsNoTracking()
            .Where(candidate => candidate.Id == deviceRowId)
            .Select(candidate => new ScheduleBundleDeviceSnapshot(
                candidate.Id,
                candidate.DeviceId,
                candidate.ScheduleBundleVersion,
                candidate.ConfigScheduleEnabled,
                candidate.ConfigScheduleFallbackProfileId,
                candidate.ConfigOverrideUntil,
                candidate.ConfigVersion))
            .SingleOrDefaultAsync(cancellationToken);

        if (device is null)
        {
            return null;
        }

        List<DeviceConfigProfile> profiles = await context.DeviceConfigProfiles
            .AsNoTracking()
            .Where(profile => profile.DeviceId == deviceRowId)
            .ToListAsync(cancellationToken);

        List<DeviceConfigScheduleRule> rules = await context.DeviceConfigScheduleRules
            .AsNoTracking()
            .Where(rule => rule.DeviceId == deviceRowId && rule.IsEnabled)
            .ToListAsync(cancellationToken);

        ScheduleBundleOverrideDto? liveOverride =
            await LoadOverrideAsync(context, device, DateTime.UtcNow, cancellationToken);

        return Compose(device, profiles, rules, liveOverride);
    }

    /// <summary>Builds the bundle for every active device, for the reconnect sweep.</summary>
    /// <param name="context">Database context to read through.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>One publication per active device.</returns>
    public async Task<List<DeviceScheduleBundlePublication>> BuildAllAsync(
        CarPosDbContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        DateTime now = DateTime.UtcNow;

        List<ScheduleBundleDeviceSnapshot> devices = await context.Devices
            .AsNoTracking()
            .Where(device => device.IsActive)
            .Select(device => new ScheduleBundleDeviceSnapshot(
                device.Id,
                device.DeviceId,
                device.ScheduleBundleVersion,
                device.ConfigScheduleEnabled,
                device.ConfigScheduleFallbackProfileId,
                device.ConfigOverrideUntil,
                device.ConfigVersion))
            .ToListAsync(cancellationToken);

        List<DeviceScheduleBundlePublication> publications =
            new List<DeviceScheduleBundlePublication>(devices.Count);
        if (devices.Count == 0)
        {
            return publications;
        }

        List<Guid> deviceRowIds = devices.Select(device => device.RowId).ToList();

        ILookup<Guid, DeviceConfigProfile> profilesByDevice = (await context.DeviceConfigProfiles
                .AsNoTracking()
                .Where(profile => deviceRowIds.Contains(profile.DeviceId))
                .ToListAsync(cancellationToken))
            .ToLookup(profile => profile.DeviceId);

        ILookup<Guid, DeviceConfigScheduleRule> rulesByDevice = (await context.DeviceConfigScheduleRules
                .AsNoTracking()
                .Where(rule => deviceRowIds.Contains(rule.DeviceId) && rule.IsEnabled)
                .ToListAsync(cancellationToken))
            .ToLookup(rule => rule.DeviceId);

        Dictionary<Guid, DeviceConfigVersion> inForceByDevice =
            await LoadInForceForOverriddenAsync(context, devices, now, cancellationToken);

        foreach (ScheduleBundleDeviceSnapshot device in devices)
        {
            ScheduleBundleOverrideDto? liveOverride = null;
            if (device.OverrideUntil is not null
                && device.OverrideUntil > now
                && inForceByDevice.TryGetValue(device.RowId, out DeviceConfigVersion? inForce))
            {
                liveOverride = ToOverride(device.OverrideUntil.Value, inForce);
            }

            publications.Add(new DeviceScheduleBundlePublication(
                device.DeviceId,
                Compose(
                    device,
                    profilesByDevice[device.RowId].ToList(),
                    rulesByDevice[device.RowId].ToList(),
                    liveOverride)));
        }

        return publications;
    }

    /// <summary>Renders an instant in the only format the firmware parses.</summary>
    /// <param name="instant">The instant. An unspecified kind is taken to be UTC, which every timestamp in this application is.</param>
    /// <returns>The instant as YYYY-MM-DDTHH:MM:SSZ.</returns>
    public static string FormatDeviceInstant(DateTime instant)
    {
        DateTime utc = instant.Kind switch
        {
            DateTimeKind.Utc => instant,
            DateTimeKind.Local => instant.ToUniversalTime(),
            _ => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
        };

        return utc.ToString(DeviceInstantFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Assembles the bundle from pieces already loaded. Pure — no database, no clock —
    /// so both loading paths above produce identical output from identical rows.
    /// </summary>
    /// <param name="device">The device's schedule columns.</param>
    /// <param name="profiles">Every profile the device has.</param>
    /// <param name="rules">The device's enabled rules only.</param>
    /// <param name="liveOverride">The override in force, or null.</param>
    /// <returns>The bundle to publish.</returns>
    private static DeviceScheduleBundleDto Compose(
        ScheduleBundleDeviceSnapshot device,
        List<DeviceConfigProfile> profiles,
        List<DeviceConfigScheduleRule> rules,
        ScheduleBundleOverrideDto? liveOverride)
    {
        Dictionary<Guid, int> slotByProfileId = profiles.ToDictionary(
            profile => profile.Id,
            profile => profile.ScheduleSlot);

        List<ScheduleBundleProfileDto> profileDtos = profiles
            .OrderBy(profile => profile.ScheduleSlot)
            .Select(profile => new ScheduleBundleProfileDto(
                profile.ScheduleSlot,
                profile.Name,
                profile.IntervalSeconds,
                profile.SleepBetween,
                profile.FixTimeoutSeconds,
                profile.QueueMaxFixes,
                profile.RetryIntervalHours,
                profile.RetryMaxAgeHours,
                profile.ConfigCheckSeconds))
            .ToList();

        // The ordinal is a dense rank over (CreatedAt, Id) — precisely the pair
        // ScheduleEvaluator.Beats falls back to once priorities tie. Ranking globally
        // rather than within each priority group is safe and simpler: the relative
        // order inside any group is unchanged by the rows around it.
        List<DeviceConfigScheduleRule> ranked = rules
            .OrderBy(rule => rule.CreatedAt)
            .ThenBy(rule => rule.Id)
            .ToList();

        List<ScheduleBundleRuleDto> ruleDtos = new List<ScheduleBundleRuleDto>(ranked.Count);
        for (int ordinal = 0; ordinal < ranked.Count; ordinal++)
        {
            DeviceConfigScheduleRule rule = ranked[ordinal];

            // A rule whose profile has gone is dropped rather than sent with a dangling
            // slot. The foreign key makes this unreachable through the service; it is a
            // repair path for rows edited by hand, and the device must never be handed
            // a window that selects nothing.
            if (!slotByProfileId.TryGetValue(rule.ProfileId, out int profileSlot))
            {
                continue;
            }

            ruleDtos.Add(new ScheduleBundleRuleDto(
                profileSlot,
                rule.DaysMaskUtc,
                rule.StartMinuteUtc,
                rule.DurationMinutes,
                rule.Priority,
                ordinal));
        }

        int fallbackSlot = DeviceScheduleBundleDto.NoFallbackSlot;
        if (device.FallbackProfileId is not null
            && slotByProfileId.TryGetValue(device.FallbackProfileId.Value, out int resolvedFallback))
        {
            fallbackSlot = resolvedFallback;
        }

        return new DeviceScheduleBundleDto(
            device.ScheduleBundleVersion,
            device.ScheduleEnabled,
            fallbackSlot,
            profileDtos,
            ruleDtos,
            liveOverride);
    }

    /// <summary>
    /// Fetches the in-force revision for the devices actually under a live override,
    /// which on a normal fleet is none of them.
    /// </summary>
    /// <param name="context">Database context to read through.</param>
    /// <param name="devices">Every device in the sweep.</param>
    /// <param name="now">The instant to judge liveness at.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The in-force revision per overridden device.</returns>
    private static async Task<Dictionary<Guid, DeviceConfigVersion>> LoadInForceForOverriddenAsync(
        CarPosDbContext context,
        List<ScheduleBundleDeviceSnapshot> devices,
        DateTime now,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, int> wantedVersionByDevice = devices
            .Where(device => device.OverrideUntil is not null && device.OverrideUntil > now)
            .ToDictionary(device => device.RowId, device => device.ConfigVersion);

        if (wantedVersionByDevice.Count == 0)
        {
            return new Dictionary<Guid, DeviceConfigVersion>();
        }

        List<Guid> overridden = wantedVersionByDevice.Keys.ToList();

        List<DeviceConfigVersion> candidates = await context.DeviceConfigVersions
            .AsNoTracking()
            .Where(version => overridden.Contains(version.DeviceId))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(version => wantedVersionByDevice[version.DeviceId] == version.Version)
            .ToDictionary(version => version.DeviceId);
    }

    /// <summary>Loads the override for one device, if one is live.</summary>
    /// <param name="context">Database context to read through.</param>
    /// <param name="device">The device's schedule columns.</param>
    /// <param name="now">The instant to judge liveness at.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The override, or null when none is in force.</returns>
    private static async Task<ScheduleBundleOverrideDto?> LoadOverrideAsync(
        CarPosDbContext context,
        ScheduleBundleDeviceSnapshot device,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (device.OverrideUntil is null || device.OverrideUntil <= now)
        {
            return null;
        }

        // The values are whatever revision is currently in force. That covers both
        // producers of an override with one rule: a person saving the settings form has
        // just made their values the in-force revision, and so has the reconciler when
        // it corrects a device.
        DeviceConfigVersion? inForce = await context.DeviceConfigVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                version => version.DeviceId == device.RowId && version.Version == device.ConfigVersion,
                cancellationToken);

        return inForce is null ? null : ToOverride(device.OverrideUntil.Value, inForce);
    }

    /// <summary>Projects an in-force revision into the bundle's override shape.</summary>
    /// <param name="until">When the override lapses.</param>
    /// <param name="inForce">The revision supplying the values.</param>
    /// <returns>The override.</returns>
    private static ScheduleBundleOverrideDto ToOverride(DateTime until, DeviceConfigVersion inForce)
    {
        return new ScheduleBundleOverrideDto(
            FormatDeviceInstant(until),
            inForce.IntervalSeconds,
            inForce.SleepBetween,
            inForce.FixTimeoutSeconds,
            inForce.QueueMaxFixes,
            inForce.RetryIntervalHours,
            inForce.RetryMaxAgeHours,
            inForce.ConfigCheckSeconds);
    }
}
