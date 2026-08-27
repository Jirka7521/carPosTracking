using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Implements <see cref="IDeviceConfigScheduleService"/>.
///
/// <para>
/// Every public method has the same three-part shape: resolve the caller's grant and
/// return before any query is shaped by user input, make the change, then rebuild and
/// return the whole state. The rebuild is not incidental — see the interface for why
/// every mutation must answer with it.
/// </para>
///
/// <para>
/// <b>An edit applies immediately.</b> Enabling a schedule, retuning the profile that
/// is currently in force, or deleting the rule that was winning all change what the
/// device should be running <em>now</em>, and waiting up to thirty seconds for the
/// worker to notice would make the panel look broken in exactly the moment somebody is
/// watching it. The worker remains the thing that handles time passing; this handles
/// intent changing.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class DeviceConfigScheduleService : IDeviceConfigScheduleService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly IDeviceConfigRevisionWriter _revisionWriter;
    private readonly ScheduleEvaluator _evaluator;
    private readonly ILogger<DeviceConfigScheduleService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on a device.</param>
    /// <param name="revisionWriter">Appends and publishes revisions; shares this context.</param>
    /// <param name="evaluator">The pure schedule arithmetic.</param>
    /// <param name="logger">Structured logger.</param>
    public DeviceConfigScheduleService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        IDeviceConfigRevisionWriter revisionWriter,
        ScheduleEvaluator evaluator,
        ILogger<DeviceConfigScheduleService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _revisionWriter = revisionWriter;
        _evaluator = evaluator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> GetStateAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await AuthorizeAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return NotVisible();
        }

        if (!access.Permissions.CanModifySettings)
        {
            return NoPermission("view");
        }

        Device? device = await LoadDeviceAsync(access.DeviceRowId, cancellationToken);
        if (device is null)
        {
            return NotVisible();
        }

        return await BuildStateAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> UpdateSettingsAsync(
        int userId,
        string deviceId,
        UpdateDeviceScheduleRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;

        if (request.FallbackProfileId is not null
            && !await ProfileBelongsAsync(device.Id, request.FallbackProfileId.Value, cancellationToken))
        {
            return OperationResult<DeviceScheduleStateDto>.Invalid(
                "The fallback profile does not belong to this device.");
        }

        if (request.Enabled && request.FallbackProfileId is null)
        {
            // Enabling without a fallback would leave every hour no rule covers with no
            // defined answer, and the device would keep whatever it last happened to be
            // given — the exact behaviour a schedule exists to remove.
            return OperationResult<DeviceScheduleStateDto>.Invalid(
                "Choose a fallback profile before enabling the schedule. It is what the "
                + "device runs at any time no rule covers.");
        }

        device.ConfigScheduleEnabled = request.Enabled;
        device.ConfigScheduleFallbackProfileId = request.FallbackProfileId;

        if (!request.Enabled)
        {
            // A stale override outliving the schedule that produced it would silently
            // suppress the worker for hours after the schedule came back on.
            device.ConfigOverrideUntil = null;
        }

        _logger.LogInformation(
            "User {UserId} {Action} the schedule for device {DeviceId}",
            userId,
            request.Enabled ? "enabled" : "disabled",
            deviceId);

        return await ApplyAndBuildAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> CreateProfileAsync(
        int userId,
        string deviceId,
        SaveConfigProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;
        string name = request.Name.Trim();

        int existingCount = await _context.DeviceConfigProfiles
            .CountAsync(profile => profile.DeviceId == device.Id, cancellationToken);
        if (existingCount >= ScheduleRules.MaxProfilesPerDevice)
        {
            return OperationResult<DeviceScheduleStateDto>.Conflict(
                $"This device already has the maximum of {ScheduleRules.MaxProfilesPerDevice} profiles.");
        }

        if (await NameTakenAsync(device.Id, name, exceptProfileId: null, cancellationToken))
        {
            return OperationResult<DeviceScheduleStateDto>.Conflict(
                $"This device already has a profile called \"{name}\".");
        }

        DateTime now = DateTime.UtcNow;
        _context.DeviceConfigProfiles.Add(new DeviceConfigProfile
        {
            DeviceId = device.Id,
            Name = name,
            IntervalSeconds = request.IntervalSeconds,
            SleepBetween = request.SleepBetween,
            FixTimeoutSeconds = request.FixTimeoutSeconds,
            QueueMaxFixes = request.QueueMaxFixes,
            RetryIntervalHours = request.RetryIntervalHours,
            RetryMaxAgeHours = request.RetryMaxAgeHours,
            ConfigCheckSeconds = request.ConfigCheckSeconds,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await _context.SaveChangesAsync(cancellationToken);

        // No re-apply: a brand-new profile is not referenced by any rule or by the
        // fallback yet, so it cannot be the one in force.
        return await BuildStateAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> UpdateProfileAsync(
        int userId,
        string deviceId,
        Guid profileId,
        SaveConfigProfileRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;
        string name = request.Name.Trim();

        DeviceConfigProfile? profile = await _context.DeviceConfigProfiles
            .SingleOrDefaultAsync(
                candidate => candidate.Id == profileId && candidate.DeviceId == device.Id,
                cancellationToken);
        if (profile is null)
        {
            return OperationResult<DeviceScheduleStateDto>.NotFound("No such profile.");
        }

        if (await NameTakenAsync(device.Id, name, profileId, cancellationToken))
        {
            return OperationResult<DeviceScheduleStateDto>.Conflict(
                $"This device already has a profile called \"{name}\".");
        }

        profile.Name = name;
        profile.IntervalSeconds = request.IntervalSeconds;
        profile.SleepBetween = request.SleepBetween;
        profile.FixTimeoutSeconds = request.FixTimeoutSeconds;
        profile.QueueMaxFixes = request.QueueMaxFixes;
        profile.RetryIntervalHours = request.RetryIntervalHours;
        profile.RetryMaxAgeHours = request.RetryMaxAgeHours;
        profile.ConfigCheckSeconds = request.ConfigCheckSeconds;
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Re-applied, because this profile may be the one in force — retuning "Night"
        // at midnight should change the tracker at midnight, not at 06:00.
        return await ApplyAndBuildAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> DeleteProfileAsync(
        int userId,
        string deviceId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;

        DeviceConfigProfile? profile = await _context.DeviceConfigProfiles
            .SingleOrDefaultAsync(
                candidate => candidate.Id == profileId && candidate.DeviceId == device.Id,
                cancellationToken);
        if (profile is null)
        {
            return OperationResult<DeviceScheduleStateDto>.NotFound("No such profile.");
        }

        // Checked here rather than left to the foreign key, so the answer is a sentence
        // with a number in it instead of a constraint-violation 500. The FK stays as the
        // backstop against a race between two deletes.
        int referencingRules = await _context.DeviceConfigScheduleRules
            .CountAsync(rule => rule.ProfileId == profileId, cancellationToken);
        if (referencingRules > 0)
        {
            return OperationResult<DeviceScheduleStateDto>.Conflict(
                $"\"{profile.Name}\" is used by {referencingRules} rule(s). Delete or repoint them first.");
        }

        if (device.ConfigScheduleFallbackProfileId == profileId)
        {
            return OperationResult<DeviceScheduleStateDto>.Conflict(
                $"\"{profile.Name}\" is this schedule's fallback profile. Choose a different one first.");
        }

        _context.DeviceConfigProfiles.Remove(profile);
        await _context.SaveChangesAsync(cancellationToken);

        // Nothing referenced it, so nothing that is in force can have changed.
        return await BuildStateAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> CreateRuleAsync(
        int userId,
        string deviceId,
        SaveScheduleRuleRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;

        if (!await ProfileBelongsAsync(device.Id, request.ProfileId, cancellationToken))
        {
            return OperationResult<DeviceScheduleStateDto>.Invalid(
                "That profile does not belong to this device.");
        }

        int existingCount = await _context.DeviceConfigScheduleRules
            .CountAsync(rule => rule.DeviceId == device.Id, cancellationToken);
        if (existingCount >= ScheduleRules.MaxRulesPerDevice)
        {
            return OperationResult<DeviceScheduleStateDto>.Conflict(
                $"This device already has the maximum of {ScheduleRules.MaxRulesPerDevice} rules.");
        }

        _context.DeviceConfigScheduleRules.Add(new DeviceConfigScheduleRule
        {
            DeviceId = device.Id,
            ProfileId = request.ProfileId,
            DaysMaskUtc = request.DaysMaskUtc,
            StartMinuteUtc = request.StartMinuteUtc,
            DurationMinutes = request.DurationMinutes,
            Priority = request.Priority,
            IsEnabled = request.IsEnabled,
            CreatedAt = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync(cancellationToken);

        return await ApplyAndBuildAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> UpdateRuleAsync(
        int userId,
        string deviceId,
        Guid ruleId,
        SaveScheduleRuleRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;

        DeviceConfigScheduleRule? rule = await _context.DeviceConfigScheduleRules
            .SingleOrDefaultAsync(
                candidate => candidate.Id == ruleId && candidate.DeviceId == device.Id,
                cancellationToken);
        if (rule is null)
        {
            return OperationResult<DeviceScheduleStateDto>.NotFound("No such rule.");
        }

        if (!await ProfileBelongsAsync(device.Id, request.ProfileId, cancellationToken))
        {
            return OperationResult<DeviceScheduleStateDto>.Invalid(
                "That profile does not belong to this device.");
        }

        rule.ProfileId = request.ProfileId;
        rule.DaysMaskUtc = request.DaysMaskUtc;
        rule.StartMinuteUtc = request.StartMinuteUtc;
        rule.DurationMinutes = request.DurationMinutes;
        rule.Priority = request.Priority;
        rule.IsEnabled = request.IsEnabled;

        await _context.SaveChangesAsync(cancellationToken);

        return await ApplyAndBuildAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> DeleteRuleAsync(
        int userId,
        string deviceId,
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;

        DeviceConfigScheduleRule? rule = await _context.DeviceConfigScheduleRules
            .SingleOrDefaultAsync(
                candidate => candidate.Id == ruleId && candidate.DeviceId == device.Id,
                cancellationToken);
        if (rule is null)
        {
            return OperationResult<DeviceScheduleStateDto>.NotFound("No such rule.");
        }

        _context.DeviceConfigScheduleRules.Remove(rule);
        await _context.SaveChangesAsync(cancellationToken);

        // Deleting the rule that was winning changes what is in force right now.
        return await ApplyAndBuildAsync(device, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceScheduleStateDto>> ResumeAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        OperationResult<Device> gate = await AuthorizeForWriteAsync(userId, deviceId, cancellationToken);
        if (!gate.IsSuccess)
        {
            return Propagate(gate);
        }

        Device device = gate.Value!;

        if (!device.ConfigScheduleEnabled)
        {
            return OperationResult<DeviceScheduleStateDto>.Invalid(
                "This device has no schedule to resume.");
        }

        device.ConfigOverrideUntil = null;

        _logger.LogInformation(
            "User {UserId} ended the settings override on device {DeviceId}",
            userId,
            deviceId);

        return await ApplyAndBuildAsync(device, cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Applying and reading back
    // -----------------------------------------------------------------------

    /// <summary>
    /// Puts the profile the schedule currently selects into force, then builds the
    /// state. Does nothing when the schedule is off or an override is live.
    /// </summary>
    /// <param name="device">The tracked device row, with any pending edits staged.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The recomputed state.</returns>
    private async Task<OperationResult<DeviceScheduleStateDto>> ApplyAndBuildAsync(
        Device device,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;

        if (device.ConfigScheduleEnabled && !IsOverrideLive(device, now))
        {
            List<ScheduleRuleSnapshot> rules = await LoadRuleSnapshotsAsync(device.Id, cancellationToken);
            ScheduleEvaluation evaluation =
                _evaluator.Evaluate(rules, device.ConfigScheduleFallbackProfileId, now);

            if (evaluation.ActiveProfileId is not null)
            {
                DeviceConfigValuesDto? values = await LoadProfileValuesAsync(
                    evaluation.ActiveProfileId.Value,
                    cancellationToken);

                if (values is not null)
                {
                    // Staged on the tracked row, so it commits inside the writer's
                    // transaction along with the revision.
                    device.ConfigScheduleEvaluatedAt = now;

                    await _revisionWriter.ApplyAsync(
                        device.Id,
                        values,
                        authorUserId: null,
                        ConfigRevisionSource.Schedule,
                        evaluation.ActiveProfileId,
                        cancellationToken);
                }
            }
        }

        return await BuildStateAsync(device, cancellationToken);
    }

    /// <summary>Assembles the whole schedule state for one device.</summary>
    /// <param name="device">The device row, already authorised.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The state.</returns>
    private async Task<OperationResult<DeviceScheduleStateDto>> BuildStateAsync(
        Device device,
        CancellationToken cancellationToken)
    {
        List<DeviceConfigProfile> profiles = await _context.DeviceConfigProfiles
            .AsNoTracking()
            .Where(profile => profile.DeviceId == device.Id)
            .OrderBy(profile => profile.Name)
            .ToListAsync(cancellationToken);

        // The author's name comes from a correlated subquery, the same trick
        // DeviceConfigService.VersionProjection uses — one round trip regardless of how
        // many profiles a device has.
        Dictionary<Guid, string?> authorByProfileId = await _context.DeviceConfigProfiles
            .AsNoTracking()
            .Where(profile => profile.DeviceId == device.Id)
            .Select(profile => new ProfileAuthor(
                profile.Id,
                _context.Users
                    .Where(user => user.Id == profile.CreatedByUserId)
                    .Select(user => user.FirstName + " " + user.LastName)
                    .FirstOrDefault()))
            .ToDictionaryAsync(row => row.ProfileId, row => row.DisplayName, cancellationToken);

        List<DeviceConfigScheduleRule> rules = await _context.DeviceConfigScheduleRules
            .AsNoTracking()
            .Where(rule => rule.DeviceId == device.Id)
            // Evaluation order, so the list on screen reads in the order the rules are
            // actually resolved in — a reader tracing an overlap goes top to bottom.
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.CreatedAt)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> nameByProfileId = profiles.ToDictionary(
            profile => profile.Id,
            profile => profile.Name);

        List<DeviceConfigProfileDto> profileDtos = profiles
            .Select(profile => new DeviceConfigProfileDto(
                profile.Id,
                profile.Name,
                ToValues(profile),
                profile.CreatedAt,
                profile.UpdatedAt,
                authorByProfileId.GetValueOrDefault(profile.Id)))
            .ToList();

        List<DeviceScheduleRuleDto> ruleDtos = rules
            .Select(rule => new DeviceScheduleRuleDto(
                rule.Id,
                rule.ProfileId,
                // The foreign key guarantees the profile exists, so the fallback string
                // is unreachable — but a KeyNotFoundException here would be a 500 on a
                // read, and no rendering of a rule list is worth that.
                nameByProfileId.GetValueOrDefault(rule.ProfileId, "(unknown)"),
                rule.DaysMaskUtc,
                rule.StartMinuteUtc,
                rule.DurationMinutes,
                rule.Priority,
                rule.IsEnabled))
            .ToList();

        DateTime now = DateTime.UtcNow;
        DeviceScheduleStatusDto? status = null;
        DeviceScheduleOverrideDto? overrideDto = null;

        if (device.ConfigScheduleEnabled)
        {
            List<ScheduleRuleSnapshot> snapshots = rules
                .Where(rule => rule.IsEnabled)
                .Select(rule => new ScheduleRuleSnapshot(
                    rule.Id,
                    rule.ProfileId,
                    rule.DaysMaskUtc,
                    rule.StartMinuteUtc,
                    rule.DurationMinutes,
                    rule.Priority,
                    rule.CreatedAt))
                .ToList();

            ScheduleEvaluation evaluation =
                _evaluator.Evaluate(snapshots, device.ConfigScheduleFallbackProfileId, now);

            status = new DeviceScheduleStatusDto(
                evaluation.ActiveProfileId,
                NameOf(nameByProfileId, evaluation.ActiveProfileId),
                evaluation.ActiveRuleId,
                evaluation.ActiveSince,
                evaluation.NextChangeAt,
                evaluation.NextProfileId,
                NameOf(nameByProfileId, evaluation.NextProfileId));

            if (IsOverrideLive(device, now))
            {
                // Evaluated *at the expiry instant* rather than reusing NextProfileId,
                // because the rules may have been edited since the override was stamped
                // — and then the profile taking over is not the one that would have.
                ScheduleEvaluation atExpiry = _evaluator.Evaluate(
                    snapshots,
                    device.ConfigScheduleFallbackProfileId,
                    device.ConfigOverrideUntil!.Value);

                overrideDto = new DeviceScheduleOverrideDto(
                    device.ConfigOverrideUntil.Value,
                    atExpiry.ActiveProfileId,
                    NameOf(nameByProfileId, atExpiry.ActiveProfileId));
            }
        }

        return OperationResult<DeviceScheduleStateDto>.Success(new DeviceScheduleStateDto(
            device.ConfigScheduleEnabled,
            device.ConfigScheduleFallbackProfileId,
            profileDtos,
            ruleDtos,
            status,
            overrideDto,
            device.ConfigScheduleEvaluatedAt));
    }

    // -----------------------------------------------------------------------
    // Small shared pieces
    // -----------------------------------------------------------------------

    /// <summary>Resolves the caller's grant on a device.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The grant, or null when the device is not visible to this caller.</returns>
    private Task<DeviceAccessContext?> AuthorizeAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        return _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
    }

    /// <summary>
    /// The permission gate every mutation shares: visible, may change settings, still
    /// active — and returns the tracked row so the caller does not query it again.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The tracked device on success, otherwise the failure to return.</returns>
    private async Task<OperationResult<Device>> AuthorizeForWriteAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await AuthorizeAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<Device>.NotFound("No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<Device>.Forbidden(
                "You do not have permission to change this device's settings.");
        }

        if (!access.IsActive)
        {
            // Matching DeviceConfigService: a retired device's ingest is rejected anyway,
            // so scheduling settings for it would be theatre. Invalid rather than
            // NotFound, because the caller can see it and pretending otherwise confuses.
            return OperationResult<Device>.Invalid(
                "This device has been deleted, so its schedule can no longer be changed.");
        }

        Device? device = await LoadDeviceAsync(access.DeviceRowId, cancellationToken);
        return device is null
            ? OperationResult<Device>.NotFound("No such device.")
            : OperationResult<Device>.Success(device);
    }

    /// <summary>Loads a device row for tracking.</summary>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tracked row, or null.</returns>
    private Task<Device?> LoadDeviceAsync(Guid deviceRowId, CancellationToken cancellationToken)
    {
        return _context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == deviceRowId, cancellationToken);
    }

    /// <summary>Loads the enabled rules of one device in the evaluator's shape.</summary>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The snapshots.</returns>
    private async Task<List<ScheduleRuleSnapshot>> LoadRuleSnapshotsAsync(
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        return await _context.DeviceConfigScheduleRules
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
    }

    /// <summary>Reads one profile's seven values.</summary>
    /// <param name="profileId">The profile.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The values, or null when the profile has gone.</returns>
    private async Task<DeviceConfigValuesDto?> LoadProfileValuesAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        return await _context.DeviceConfigProfiles
            .AsNoTracking()
            .Where(profile => profile.Id == profileId)
            .Select(profile => new DeviceConfigValuesDto(
                profile.IntervalSeconds,
                profile.SleepBetween,
                profile.FixTimeoutSeconds,
                profile.QueueMaxFixes,
                profile.RetryIntervalHours,
                profile.RetryMaxAgeHours,
                profile.ConfigCheckSeconds))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Whether a profile exists and belongs to this device.</summary>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="profileId">The profile being referenced.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>True when the reference is legitimate.</returns>
    private Task<bool> ProfileBelongsAsync(
        Guid deviceRowId,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        // Checked explicitly rather than left to the foreign key, which would happily
        // accept another device's profile — the FK only says "some profile", and a rule
        // pointing at a neighbouring tracker's settings is exactly the kind of mistake
        // that would go unnoticed until something reported every five seconds.
        return _context.DeviceConfigProfiles
            .AnyAsync(
                profile => profile.Id == profileId && profile.DeviceId == deviceRowId,
                cancellationToken);
    }

    /// <summary>Whether another profile of this device already has that name.</summary>
    /// <param name="deviceRowId">Internal device id.</param>
    /// <param name="name">The candidate name, already trimmed.</param>
    /// <param name="exceptProfileId">The profile being renamed, excluded from the check.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>True when the name is taken.</returns>
    private Task<bool> NameTakenAsync(
        Guid deviceRowId,
        string name,
        Guid? exceptProfileId,
        CancellationToken cancellationToken)
    {
        // Case-insensitive: "Night" and "night" beside each other would make every rule
        // list ambiguous to read, which is the whole reason profiles have names.
        // Translated by Npgsql to lower(name) = lower(@p), so it runs in SQL.
        return _context.DeviceConfigProfiles
            .AnyAsync(
                profile => profile.DeviceId == deviceRowId
                    && profile.Id != exceptProfileId
                    && profile.Name.ToLower() == name.ToLower(),
                cancellationToken);
    }

    /// <summary>Whether a manual override is still holding the schedule off.</summary>
    /// <param name="device">The device row.</param>
    /// <param name="now">The current instant (UTC).</param>
    /// <returns>True while the override has not lapsed.</returns>
    private static bool IsOverrideLive(Device device, DateTime now)
    {
        return device.ConfigOverrideUntil is not null && device.ConfigOverrideUntil > now;
    }

    /// <summary>Looks a profile name up, tolerating a null id.</summary>
    /// <param name="nameByProfileId">Names of this device's profiles.</param>
    /// <param name="profileId">The profile to name, or null.</param>
    /// <returns>The name, or null.</returns>
    private static string? NameOf(Dictionary<Guid, string> nameByProfileId, Guid? profileId)
    {
        return profileId is not null && nameByProfileId.TryGetValue(profileId.Value, out string? name)
            ? name
            : null;
    }

    /// <summary>Projects a profile row onto the shared settings shape.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>Its seven values.</returns>
    private static DeviceConfigValuesDto ToValues(DeviceConfigProfile profile)
    {
        return new DeviceConfigValuesDto(
            profile.IntervalSeconds,
            profile.SleepBetween,
            profile.FixTimeoutSeconds,
            profile.QueueMaxFixes,
            profile.RetryIntervalHours,
            profile.RetryMaxAgeHours,
            profile.ConfigCheckSeconds);
    }

    /// <summary>Re-types a failed gate result as a state result, keeping outcome and detail.</summary>
    /// <param name="gate">The failed authorisation result.</param>
    /// <returns>The same failure, typed for the caller's signature.</returns>
    private static OperationResult<DeviceScheduleStateDto> Propagate(OperationResult<Device> gate)
    {
        return new OperationResult<DeviceScheduleStateDto>(gate.Outcome, null, gate.Detail);
    }

    /// <summary>The 404 every unreachable device gets — never a 403, which would confirm it exists.</summary>
    /// <returns>A NotFound result.</returns>
    private static OperationResult<DeviceScheduleStateDto> NotVisible()
    {
        return OperationResult<DeviceScheduleStateDto>.NotFound("No such device.");
    }

    /// <summary>The 403 for a caller who can see the device but not its settings.</summary>
    /// <param name="verb">What they were trying to do, for the message.</param>
    /// <returns>A Forbidden result.</returns>
    private static OperationResult<DeviceScheduleStateDto> NoPermission(string verb)
    {
        return OperationResult<DeviceScheduleStateDto>.Forbidden(
            $"You do not have permission to {verb} this device's schedule.");
    }
}
