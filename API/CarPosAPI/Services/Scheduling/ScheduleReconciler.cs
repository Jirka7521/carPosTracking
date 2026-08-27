using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Devices;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Implements <see cref="IScheduleReconciler"/>.
///
/// <para>
/// The pass is written as <b>four queries for the whole fleet</b>, not four per device:
/// the candidate devices, their rules, their profiles, and one set-based stamp at the
/// end. Only the devices that actually need a change cost anything more, and at a
/// couple of switches a day that is a handful of writes against thousands of quiet
/// passes. A per-device loop of lookups here would be the N+1 this project's rules
/// forbid, running every thirty seconds for ever.
/// </para>
/// </summary>
internal sealed class ScheduleReconciler : IScheduleReconciler
{
    private readonly CarPosDbContext _context;
    private readonly ScheduleEvaluator _evaluator;
    private readonly IDeviceConfigRevisionWriter _revisionWriter;
    private readonly ILogger<ScheduleReconciler> _logger;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="context">Scoped database context, created per pass by the worker.</param>
    /// <param name="evaluator">The pure schedule arithmetic.</param>
    /// <param name="revisionWriter">Appends and publishes a revision when one is needed.</param>
    /// <param name="logger">Structured logger.</param>
    public ScheduleReconciler(
        CarPosDbContext context,
        ScheduleEvaluator evaluator,
        IDeviceConfigRevisionWriter revisionWriter,
        ILogger<ScheduleReconciler> logger)
    {
        _context = context;
        _evaluator = evaluator;
        _revisionWriter = revisionWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ReconcileAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        // Lapsed overrides are cleared first and set-based, so the query below can be a
        // plain "no override" filter. Doing it here rather than letting each read treat
        // a past instant as absent also means the column tells the truth on inspection:
        // a value in it always means an override is live.
        await _context.Devices
            .Where(device => device.ConfigOverrideUntil != null && device.ConfigOverrideUntil <= utcNow)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(device => device.ConfigOverrideUntil, (DateTime?)null),
                cancellationToken);

        List<ScheduledDeviceSnapshot> devices = await _context.Devices
            .AsNoTracking()
            .Where(device => device.IsActive
                && device.ConfigScheduleEnabled
                && device.ConfigOverrideUntil == null)
            .Select(device => new ScheduledDeviceSnapshot(
                device.Id,
                device.DeviceId,
                device.ConfigScheduleFallbackProfileId))
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
        {
            return 0;
        }

        List<Guid> deviceRowIds = devices.Select(device => device.RowId).ToList();

        // Both of these are one query for the whole fleet, grouped in memory. The fleet
        // is small and the rows are tiny; the alternative — a query per device — is the
        // shape that gets slower the more devices there are, which is precisely backwards.
        ILookup<Guid, ScheduleRuleSnapshot> rulesByDevice = (await _context.DeviceConfigScheduleRules
                .AsNoTracking()
                .Where(rule => deviceRowIds.Contains(rule.DeviceId) && rule.IsEnabled)
                .Select(rule => new DeviceRuleSnapshot(
                    rule.DeviceId,
                    new ScheduleRuleSnapshot(
                        rule.Id,
                        rule.ProfileId,
                        rule.DaysMaskUtc,
                        rule.StartMinuteUtc,
                        rule.DurationMinutes,
                        rule.Priority,
                        rule.CreatedAt)))
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.DeviceRowId, row => row.Rule);

        Dictionary<Guid, ProfileValues> profiles = (await _context.DeviceConfigProfiles
                .AsNoTracking()
                .Where(profile => deviceRowIds.Contains(profile.DeviceId))
                .Select(profile => new ProfileValues(
                    profile.Id,
                    profile.Name,
                    new DeviceConfigValuesDto(
                        profile.IntervalSeconds,
                        profile.SleepBetween,
                        profile.FixTimeoutSeconds,
                        profile.QueueMaxFixes,
                        profile.RetryIntervalHours,
                        profile.RetryMaxAgeHours,
                        profile.ConfigCheckSeconds)))
                .ToListAsync(cancellationToken))
            .ToDictionary(profile => profile.ProfileId);

        int changed = 0;

        foreach (ScheduledDeviceSnapshot device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ScheduleEvaluation evaluation = _evaluator.Evaluate(
                rulesByDevice[device.RowId].ToList(),
                device.FallbackProfileId,
                utcNow);

            if (evaluation.ActiveProfileId is null
                || !profiles.TryGetValue(evaluation.ActiveProfileId.Value, out ProfileValues? target))
            {
                // No rule matched and no fallback is set — or the fallback names a
                // profile that has since gone. Neither is reachable through the service,
                // which refuses to enable a schedule without a usable fallback, so this
                // is a repair path for rows edited by hand. Skipping is the safe answer:
                // the device keeps working on its current settings.
                _logger.LogWarning(
                    "Device {DeviceId}: schedule is enabled but resolves to no usable profile; leaving its settings alone",
                    device.DeviceId);
                continue;
            }

            // The writer is what decides whether this is actually a change: it compares
            // against the revision in force and appends nothing when they match. That is
            // why a quiet pass costs two reads per device and no writes at all.
            ConfigRevisionOutcome? outcome = await _revisionWriter.ApplyAsync(
                device.RowId,
                target.Values,
                authorUserId: null,
                ConfigRevisionSource.Schedule,
                target.ProfileId,
                cancellationToken);

            if (outcome is not null && outcome.Changed)
            {
                changed++;
                _logger.LogInformation(
                    "Device {DeviceId}: schedule applied profile {ProfileName} as revision {Version}",
                    device.DeviceId,
                    target.Name,
                    outcome.Version);
            }
        }

        // One stamp for every device the pass looked at, whether or not it changed —
        // "when was this last evaluated?" is a question about the pass, not about
        // whether it found anything to do.
        await _context.Devices
            .Where(device => deviceRowIds.Contains(device.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    device => device.ConfigScheduleEvaluatedAt,
                    (DateTime?)utcNow),
                cancellationToken);

        return changed;
    }
}
