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
/// <b>This pass verifies; it no longer drives.</b> The device evaluates its own
/// schedule from the bundle on <c>devices/&lt;id&gt;/schedule</c> and reports the
/// profile slot it is running in every fix. A pass compares that against what the rules
/// say <em>at the instant the device sampled</em>, and writes a revision only when the
/// two genuinely disagree — see <c>NeedsCorrection</c> for the four ways they can
/// differ without anything being wrong.
/// </para>
///
/// <para>
/// A device that reports no schedule version at all is driven exactly as it always was,
/// unconditionally. That is not a leftover: it is how firmware without device-side
/// scheduling keeps working, and it means this change can be deployed to a fleet of old
/// trackers with no observable effect.
/// </para>
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
    private readonly IScheduleBundlePublisher _bundlePublisher;
    private readonly ILogger<ScheduleReconciler> _logger;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="context">Scoped database context, created per pass by the worker.</param>
    /// <param name="evaluator">The pure schedule arithmetic.</param>
    /// <param name="revisionWriter">Appends and publishes a revision when one is needed.</param>
    /// <param name="bundlePublisher">Ships a corrective override to a self-switching device.</param>
    /// <param name="logger">Structured logger.</param>
    public ScheduleReconciler(
        CarPosDbContext context,
        ScheduleEvaluator evaluator,
        IDeviceConfigRevisionWriter revisionWriter,
        IScheduleBundlePublisher bundlePublisher,
        ILogger<ScheduleReconciler> logger)
    {
        _context = context;
        _evaluator = evaluator;
        _revisionWriter = revisionWriter;
        _bundlePublisher = bundlePublisher;
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
                device.ConfigScheduleFallbackProfileId,
                device.ScheduleBundleVersion,
                device.ReportedScheduleVersion,
                device.ReportedProfileSlot,
                device.ReportedProfileAt))
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
                    profile.ScheduleSlot,
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

            if (!NeedsCorrection(device, rulesByDevice[device.RowId], profiles, utcNow, target))
            {
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

            // A device that does its own switching needs more than the config document:
            // it would evaluate its own schedule at the next boundary and switch right
            // back. Stamping an override is what makes the correction stick until then.
            if (device.ReportedScheduleVersion is not null)
            {
                await OverrideUntilNextChangeAsync(device, evaluation, cancellationToken);
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

    /// <summary>
    /// Decides whether this device needs the server to intervene at all.
    ///
    /// <para>
    /// This is where the pass stopped being a driver and became a check. Before
    /// device-side scheduling every pass wrote the resolved profile through
    /// unconditionally; now the device switches itself, and the server's job is to spot
    /// the cases where it demonstrably has not.
    /// </para>
    /// </summary>
    /// <param name="device">The device being judged.</param>
    /// <param name="rules">Its enabled rules.</param>
    /// <param name="profiles">Every profile on the fleet, by id.</param>
    /// <param name="utcNow">The instant of this pass.</param>
    /// <param name="target">The profile the schedule says should be in force now.</param>
    /// <returns>True when a revision should be written.</returns>
    private bool NeedsCorrection(
        ScheduledDeviceSnapshot device,
        IEnumerable<ScheduleRuleSnapshot> rules,
        Dictionary<Guid, ProfileValues> profiles,
        DateTime utcNow,
        ProfileValues target)
    {
        // Firmware that predates device-side scheduling, or a device that has simply
        // never reported. It cannot switch itself, so the server drives it exactly as it
        // always has — this branch is the whole of the backward compatibility story, and
        // it is why deploying this change to a fleet of old trackers alters nothing.
        if (device.ReportedScheduleVersion is null || device.ReportedProfileSlot is null)
        {
            return true;
        }

        // The device is switching itself but has not received the current bundle yet.
        // Its profile will differ from ours and that is not a fault — it is a delivery
        // in flight. Nothing is republished here on purpose: the bundle is already
        // retained on the broker, the device re-subscribes on every wake and on its own
        // config-check interval, and a republish every thirty seconds until the next
        // report would be a storm, not a repair.
        if (device.ReportedScheduleVersion < device.ScheduleBundleVersion)
        {
            return false;
        }

        // Too long since we heard anything to draw a conclusion from it. See
        // ScheduleRules.StaleReportAfter for why this is generous.
        if (device.ReportedProfileAt is null
            || utcNow - device.ReportedProfileAt.Value > ScheduleRules.StaleReportAfter)
        {
            return false;
        }

        // Evaluated at the instant the device sampled, not at now. A fix taken five
        // minutes before a boundary should report the profile that was in force five
        // minutes before the boundary, and judging it against the present would make
        // every single switch look like a failure on a device with a long interval.
        ScheduleEvaluation asReported = _evaluator.Evaluate(
            rules.ToList(),
            device.FallbackProfileId,
            device.ReportedProfileAt.Value);

        if (asReported.ActiveProfileId is null
            || !profiles.TryGetValue(asReported.ActiveProfileId.Value, out ProfileValues? expected))
        {
            // The schedule resolved to nothing at that instant — a profile deleted since,
            // most likely. There is no expectation to compare against, so there is
            // nothing to call a disagreement.
            return false;
        }

        if (expected.ScheduleSlot == device.ReportedProfileSlot.Value)
        {
            return false;  // the device and the schedule agree; the common path
        }

        _logger.LogWarning(
            "Device {DeviceId}: reported profile slot {ReportedSlot} at {ReportedAt:o} but the schedule said "
            + "{ExpectedProfile} (slot {ExpectedSlot}); correcting it to {TargetProfile}",
            device.DeviceId,
            device.ReportedProfileSlot.Value,
            device.ReportedProfileAt.Value,
            expected.Name,
            expected.ScheduleSlot,
            target.Name);

        return true;
    }

    /// <summary>
    /// Stamps an override lasting until the schedule next changes, and republishes the
    /// bundle so the device honours it.
    ///
    /// <para>
    /// Deliberately self-expiring. The likeliest reason a device disagreed is that its
    /// clock has drifted — it has no crystal and re-seeds only from a GNSS fix — and an
    /// override that lapses at the next boundary means the correction costs one wrong
    /// stretch, not a tracker pinned to one profile until somebody notices.
    /// </para>
    /// </summary>
    /// <param name="device">The device being corrected.</param>
    /// <param name="evaluation">The evaluation at this pass, for its next-change instant.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>A task that completes when the override is saved and published.</returns>
    private async Task OverrideUntilNextChangeAsync(
        ScheduledDeviceSnapshot device,
        ScheduleEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        if (evaluation.NextChangeAt is null)
        {
            // A schedule that never switches has nothing for an override to expire at.
            // The config document alone will have to do — and on such a device it is
            // enough, because there is no boundary at which the device would switch away
            // from what it was just given.
            return;
        }

        Device? row = await _context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == device.RowId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.ConfigOverrideUntil = evaluation.NextChangeAt;
        await _bundlePublisher.PublishAsync(row, cancellationToken);
    }
}
