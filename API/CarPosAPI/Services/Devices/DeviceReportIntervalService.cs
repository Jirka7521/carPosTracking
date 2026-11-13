using System.Linq.Expressions;
using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Implements <see cref="IDeviceReportIntervalService"/>.
///
/// <para>
/// This class only gathers the sets of settings that might be in force; which of them
/// count and how they combine is <see cref="ReportIntervalCalculator"/>'s job, and
/// which profile a schedule selects is <see cref="ScheduleEvaluator"/>'s — the same
/// evaluator the schedule panel and the reconciler use, so the map can never disagree
/// with them about the current plan.
/// </para>
///
/// <para>
/// Only interval columns are read, never a whole revision or profile: the answer is
/// one number, and a read-only viewer is entitled to nothing more.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class DeviceReportIntervalService : IDeviceReportIntervalService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ScheduleEvaluator _evaluator;
    private readonly ReportIntervalCalculator _calculator;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on a device.</param>
    /// <param name="evaluator">The pure schedule arithmetic.</param>
    /// <param name="calculator">The pure interval arithmetic.</param>
    public DeviceReportIntervalService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        ScheduleEvaluator evaluator,
        ReportIntervalCalculator calculator)
    {
        _context = context;
        _authorizer = authorizer;
        _evaluator = evaluator;
        _calculator = calculator;
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceReportIntervalDto>> GetAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        // Any active grant will do — no CanModifySettings check. A read-only viewer
        // needs to know whether the pin they are looking at is current, and the two
        // numbers returned say nothing about how the device is configured beyond that.
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<DeviceReportIntervalDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        int? maxIntervalSeconds = await GetMaxIntervalSecondsAsync(access.DeviceRowId, cancellationToken);

        return OperationResult<DeviceReportIntervalDto>.Success(new DeviceReportIntervalDto(
            maxIntervalSeconds,
            _calculator.OverdueAfterSeconds(maxIntervalSeconds)));
    }

    /// <inheritdoc />
    public async Task<int?> GetOverdueAfterSecondsAsync(
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        int? maxIntervalSeconds = await GetMaxIntervalSecondsAsync(deviceRowId, cancellationToken);
        return _calculator.OverdueAfterSeconds(maxIntervalSeconds);
    }

    /// <summary>
    /// Loads every set of settings that might be in force and hands them to the
    /// calculator.
    /// </summary>
    /// <param name="deviceRowId">Internal device id, already authorised.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Seconds, or null when the device is retired or has no stored settings.</returns>
    private async Task<int?> GetMaxIntervalSecondsAsync(
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        Device? device = await _context.Devices
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == deviceRowId, cancellationToken);

        // A retired tracker is not expected to report at all, so it can never be late.
        if (device is null || !device.IsActive)
        {
            return null;
        }

        DateTime now = DateTime.UtcNow;

        // The same test DeviceConfigScheduleService.IsOverrideLive makes: while an
        // override holds, the device runs its config document, not the schedule.
        bool scheduleInForce = device.ConfigScheduleEnabled
            && !(device.ConfigOverrideUntil is not null && device.ConfigOverrideUntil > now);

        ReportIntervalCandidate? desired =
            await RevisionAsync(deviceRowId, device.ConfigVersion, cancellationToken);

        // Usually the same revision as the desired one, so it is only read again when
        // a change is still waiting for the device to pick it up.
        ReportIntervalCandidate? applied = device.ConfigAppliedVersion is null
            ? null
            : device.ConfigAppliedVersion == device.ConfigVersion
                ? desired
                : await RevisionAsync(deviceRowId, device.ConfigAppliedVersion.Value, cancellationToken);

        ReportIntervalCandidate? activeProfile = null;
        ReportIntervalCandidate? reportedProfile = null;

        if (scheduleInForce)
        {
            // Snapshots built exactly as DeviceConfigScheduleService builds them, so
            // "the current plan" here is the one the schedule panel shows.
            List<ScheduleRuleSnapshot> rules = await _context.DeviceConfigScheduleRules
                .AsNoTracking()
                .Where(rule => rule.DeviceId == deviceRowId && rule.IsEnabled)
                .Select(rule => new ScheduleRuleSnapshot(
                    rule.Id,
                    rule.ProfileId,
                    rule.DaysMaskUtc,
                    rule.StartMinuteUtc,
                    rule.DurationMinutes,
                    rule.Priority,
                    rule.CreatedAt))
                .ToListAsync(cancellationToken);

            Guid? activeProfileId = _evaluator
                .Evaluate(rules, device.ConfigScheduleFallbackProfileId, now)
                .ActiveProfileId;

            if (activeProfileId is not null)
            {
                activeProfile = await ProfileAsync(
                    deviceRowId,
                    profile => profile.Id == activeProfileId.Value,
                    cancellationToken);
            }

            // The device reports a slot, not an id; the slot maps to the profile that
            // holds it now. Absent until the device has reported one.
            if (device.ReportedProfileSlot is not null)
            {
                int reportedSlot = device.ReportedProfileSlot.Value;
                reportedProfile = await ProfileAsync(
                    deviceRowId,
                    profile => profile.ScheduleSlot == reportedSlot,
                    cancellationToken);
            }
        }

        return _calculator.MaxIntervalSeconds(
            scheduleInForce,
            activeProfile,
            reportedProfile,
            desired,
            applied);
    }

    /// <summary>The interval columns of one config revision.</summary>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="version">The revision number.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The revision's intervals, or null when it does not exist.</returns>
    private Task<ReportIntervalCandidate?> RevisionAsync(
        Guid deviceRowId,
        int version,
        CancellationToken cancellationToken)
    {
        return _context.DeviceConfigVersions
            .AsNoTracking()
            .Where(revision => revision.DeviceId == deviceRowId && revision.Version == version)
            .Select(revision => new ReportIntervalCandidate(
                revision.IntervalSeconds,
                revision.MotionEnabled,
                revision.MovingIntervalSeconds))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The interval columns of one of the device's schedule profiles.</summary>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="which">Picks the profile, by id or by slot.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The profile's intervals, or null when no profile matches.</returns>
    private Task<ReportIntervalCandidate?> ProfileAsync(
        Guid deviceRowId,
        Expression<Func<DeviceConfigProfile, bool>> which,
        CancellationToken cancellationToken)
    {
        return _context.DeviceConfigProfiles
            .AsNoTracking()
            .Where(profile => profile.DeviceId == deviceRowId)
            .Where(which)
            .Select(profile => new ReportIntervalCandidate(
                profile.IntervalSeconds,
                profile.MotionEnabled,
                profile.MovingIntervalSeconds))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
