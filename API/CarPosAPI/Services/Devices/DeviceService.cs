using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Implements <see cref="IDeviceService"/>.
///
/// Every method that touches a specific device starts by resolving the caller's
/// grant through <see cref="IDeviceAccessAuthorizer"/> and returns early if it
/// comes back null. That ordering is the security property: no query is shaped by
/// user input before it is known that the user may see the device at all.
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class DeviceService : IDeviceService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly IDeviceProvisioningService _provisioningService;
    private readonly ILogger<DeviceService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on a device.</param>
    /// <param name="provisioningService">Generates and renders device key material.</param>
    /// <param name="logger">Structured logger (never receives key material).</param>
    public DeviceService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        IDeviceProvisioningService provisioningService,
        ILogger<DeviceService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _provisioningService = provisioningService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceDto>> ListForUserAsync(int userId, CancellationToken cancellationToken)
    {
        // One query, three tables. The device comes through the Access
        // navigation, and the alias through a correlated subquery, so the whole
        // list costs a single round trip — adding a device can never turn this
        // into an N+1.
        //
        // The OrderBy sits *before* the projection deliberately: ordering by a
        // member of an already-constructed DTO is not reliably translatable, and
        // would either throw or silently fall back to sorting in memory.
        return await _context.Accesses
            .AsNoTracking()
            .Where(access => access.UserId == userId && access.IsActive)
            .OrderBy(access => access.Device!.DeviceId)
            .Select(access => new DeviceDto(
                access.Device!.DeviceId,
                access.Device.DisplayName,
                _context.DeviceAliases
                    .Where(alias => alias.UserId == userId && alias.DeviceId == access.DeviceId)
                    .Select(alias => alias.Alias)
                    .FirstOrDefault(),
                access.Device.IsActive,
                access.Device.CreatedAt,
                access.Device.DeactivatedAt,
                access.Device.LastSeenAt,
                // The battery from this device's most recent fix, as a correlated
                // subquery so the whole list is still one round trip. Null when the
                // device has never reported (or reported no battery).
                _context.Positions
                    .Where(position => position.DeviceId == access.DeviceId)
                    .OrderByDescending(position => position.FixTime)
                    .Select(position => position.BatteryPct)
                    .FirstOrDefault(),
                new DevicePermissionsDto(
                    access.CanRead,
                    access.CanDelete,
                    access.CanShare,
                    access.CanModifySettings)))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceCreatedDto>> CreateAsync(
        int userId,
        CreateDeviceRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The device row and the grants that make it reachable are one unit of
        // work. Committing the device alone would leave a tracker nobody can see,
        // list or delete — and whose id can never be reused, because provisioning
        // is create-only.
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        DeviceProvisioningResult provisioning =
            await _provisioningService.ProvisionAsync(_context, request, cancellationToken);

        if (provisioning.Outcome == DeviceProvisioningOutcome.DuplicateDeviceId)
        {
            return OperationResult<DeviceCreatedDto>.Conflict(
                $"A device with id '{request.DeviceId}' is already registered. Device ids are permanent.");
        }

        // The creator's grant is built from CapabilitySet.Full(), never from the
        // request: a client that could talk the server into registering a device it
        // then cannot administer would have created an orphan.
        AddGrant(provisioning.DeviceRowId, userId, userId, CapabilitySet.Full());

        // Revision 1, in the same transaction. Every device must always point at a row
        // that exists — the settings endpoints and the retained-config sweep both
        // resolve devices.config_version to one, and a device without it would answer
        // 404 on a panel the dashboard shows unconditionally. No author is recorded:
        // these are the factory defaults, not somebody's decision.
        _context.DeviceConfigVersions.Add(new DeviceConfigVersion
        {
            DeviceId = provisioning.DeviceRowId,
            Version = DeviceConfigRules.InitialVersion,
            IntervalSeconds = DeviceConfigRules.DefaultIntervalSeconds,
            SleepBetween = DeviceConfigRules.DefaultSleepBetween,
            FixTimeoutSeconds = DeviceConfigRules.DefaultFixTimeoutSeconds,
            QueueMaxFixes = DeviceConfigRules.DefaultQueueMaxFixes,
            RetryIntervalHours = DeviceConfigRules.DefaultRetryIntervalHours,
            RetryMaxAgeHours = DeviceConfigRules.DefaultRetryMaxAgeHours,
            ConfigCheckSeconds = DeviceConfigRules.DefaultConfigCheckSeconds,
            CreatedByUserId = null,
            CreatedAt = DateTime.UtcNow,
        });

        int sharedCount = await AddAdditionalGrantsAsync(
            provisioning.DeviceRowId,
            userId,
            request.AdditionalAccesses,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} registered device {DeviceId} and shared it with {SharedCount} other user(s)",
            userId,
            request.DeviceId,
            sharedCount);

        DeviceDto device = new DeviceDto(
            provisioning.Device!.DeviceId,
            provisioning.Device.DisplayName,
            // A brand-new device has no alias and has never reported, so these are
            // known without asking the database again (no last-seen, no battery).
            null,
            true,
            DateTime.UtcNow,
            null,
            null,
            null,
            new DevicePermissionsDto(true, true, true, true));

        return OperationResult<DeviceCreatedDto>.Success(new DeviceCreatedDto(device, provisioning.Device));
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DeactivateAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<bool>.NotFound("No such device.");
        }

        if (!access.Permissions.CanDelete)
        {
            return OperationResult<bool>.Forbidden("You do not have permission to delete this device.");
        }

        if (!access.IsActive)
        {
            // Already retired. Answering success keeps the operation idempotent —
            // a double-click must not produce an error.
            return OperationResult<bool>.Success(true);
        }

        Device? device = await _context.Devices
            .SingleOrDefaultAsync(candidate => candidate.Id == access.DeviceRowId, cancellationToken);

        if (device is null)
        {
            return OperationResult<bool>.NotFound("No such device.");
        }

        // Soft delete only. The rows are history: positions reference this device,
        // and the ingest pipeline's device cache treats an inactive row as "reject
        // its messages", which is precisely the wanted behaviour.
        device.IsActive = false;
        device.DeactivatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device {DeviceId} deactivated by user {UserId}", deviceId, userId);

        return OperationResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SetAliasAsync(
        int userId,
        string deviceId,
        string alias,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alias);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<bool>.NotFound("No such device.");
        }

        // No capability check beyond the grant existing: the alias is private to
        // this user and invisible to everyone else, so read access is enough. It
        // would be wrong to require CanModifySettings — that governs the *shared*
        // device settings.
        string trimmed = alias.Trim();

        DeviceAlias? existing = await _context.DeviceAliases
            .SingleOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.DeviceId == access.DeviceRowId,
                cancellationToken);

        if (trimmed.Length == 0)
        {
            // Clearing removes the row rather than storing an empty string, so
            // "no alias" has exactly one representation in the database.
            if (existing is not null)
            {
                _context.DeviceAliases.Remove(existing);
                await _context.SaveChangesAsync(cancellationToken);
            }

            return OperationResult<bool>.Success(true);
        }

        if (existing is null)
        {
            _context.DeviceAliases.Add(new DeviceAlias
            {
                UserId = userId,
                DeviceId = access.DeviceRowId,
                Alias = trimmed,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Alias = trimmed;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return OperationResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<OperationResult<DeviceProvisioningResultDto>> GetProvisioningAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<DeviceProvisioningResultDto>.NotFound("No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            // The payload contains no secret — the private key never leaves the
            // server — but it does describe the broker topics this device publishes
            // on, which is operational detail a read-only viewer has no use for.
            return OperationResult<DeviceProvisioningResultDto>.Forbidden(
                "You do not have permission to view this device's firmware configuration.");
        }

        DeviceProvisioningResultDto? payload =
            await _provisioningService.DescribeAsync(_context, access.DeviceId, cancellationToken);

        return payload is null
            ? OperationResult<DeviceProvisioningResultDto>.NotFound(
                "This device has no stored public key, so no firmware configuration can be rendered.")
            : OperationResult<DeviceProvisioningResultDto>.Success(payload);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AckKeyImportedDto>> ImportAckKeyAsync(
        int userId,
        string deviceId,
        ImportAckKeyRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (access is null)
        {
            return OperationResult<AckKeyImportedDto>.NotFound("No such device.");
        }

        // The same gate as reading the firmware configuration, and for a stronger
        // reason: this one is a write that changes how the device is talked to.
        if (!access.Permissions.CanModifySettings)
        {
            return OperationResult<AckKeyImportedDto>.Forbidden(
                "You do not have permission to change this device's firmware configuration.");
        }

        return await _provisioningService.ImportAckPublicKeyAsync(
            _context,
            access.DeviceId,
            request.AckPublicKeyPem,
            cancellationToken);
    }

    /// <summary>
    /// Turns the request's <c>additionalAccesses</c> into grant rows, resolving
    /// each email to a user.
    /// </summary>
    /// <param name="deviceRowId">The device the grants are on.</param>
    /// <param name="grantedBy">The creator, recorded for audit.</param>
    /// <param name="requested">The requested shares, possibly null or empty.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>How many grants were added.</returns>
    private async Task<int> AddAdditionalGrantsAsync(
        Guid deviceRowId,
        int grantedBy,
        IReadOnlyList<DeviceAccessGrantInputDto>? requested,
        CancellationToken cancellationToken)
    {
        if (requested is null || requested.Count == 0)
        {
            return 0;
        }

        // Normalised once, then resolved in a single IN query — looking each email
        // up inside the loop would be the textbook N+1, inside a write transaction
        // no less.
        Dictionary<string, DeviceAccessGrantInputDto> byEmail = [];
        foreach (DeviceAccessGrantInputDto entry in requested)
        {
            string email = entry.UserEmail.Trim().ToLowerInvariant();

            // Last one wins for a duplicated address. The alternative — rejecting
            // the whole request — punishes a harmless typo, and the active-grant
            // unique index would refuse the second row anyway.
            byEmail[email] = entry;
        }

        List<User> matched = await _context.Users
            .AsNoTracking()
            .Where(user => byEmail.Keys.Contains(user.Email))
            .ToListAsync(cancellationToken);

        foreach (User user in matched)
        {
            if (user.Id == grantedBy)
            {
                // The creator already has a full grant from CreateAsync; a second row
                // would violate the one-active-grant-per-pair index.
                continue;
            }

            DeviceAccessGrantInputDto entry = byEmail[user.Email];

            AddGrant(
                deviceRowId,
                user.Id,
                grantedBy,
                CapabilitySet.FromRequest(entry.CanDelete, entry.CanShare, entry.CanModifySettings));
        }

        // Unmatched addresses are skipped in silence, by contract: reporting them
        // would turn device creation into a way to test whether an email has an
        // account here.
        if (matched.Count != byEmail.Count)
        {
            _logger.LogInformation(
                "{SkippedCount} of {RequestedCount} additional access entries matched no account and were skipped",
                byEmail.Count - matched.Count,
                byEmail.Count);
        }

        return matched.Count;
    }

    /// <summary>
    /// Adds one access grant to the change tracker. <c>CanRead</c> is set here and
    /// never taken from a caller — an active grant always implies read access.
    /// </summary>
    /// <param name="deviceRowId">The device the grant is on.</param>
    /// <param name="userId">Who is being granted access.</param>
    /// <param name="grantedBy">Who granted it, recorded for audit.</param>
    /// <param name="capabilities">The coerced capability set.</param>
    private void AddGrant(Guid deviceRowId, int userId, int grantedBy, CapabilitySet capabilities)
    {
        _context.Accesses.Add(new Access
        {
            UserId = userId,
            DeviceId = deviceRowId,
            GrantedBy = grantedBy,
            CanRead = true,
            CanDelete = capabilities.CanDelete,
            CanShare = capabilities.CanShare,
            CanModifySettings = capabilities.CanModifySettings,
            IsActive = true,
            DateRegistration = DateTime.UtcNow,
        });
    }
}
