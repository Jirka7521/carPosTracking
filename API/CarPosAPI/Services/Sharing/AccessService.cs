using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Implements <see cref="IAccessService"/>.
///
/// Three safeguards run through it. Every path resolves the caller's own grant
/// first and requires <c>CanShare</c> — the id in the URL is never trusted to
/// imply permission. <c>CanShare</c> is not a master key: nobody edits their own
/// grant, and nobody hands out a capability they do not hold
/// (<see cref="GrantChangePolicy"/>). And the last remaining sharer on a device
/// cannot be removed or demoted: a device with no one able to share it can never be
/// given away or recovered, and since devices are only ever soft-deleted, that
/// state is permanent.
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class AccessService : IAccessService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ILogger<AccessService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on a device.</param>
    /// <param name="logger">Structured logger.</param>
    public AccessService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        ILogger<AccessService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<AccessDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? caller = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (caller is null)
        {
            return OperationResult<IReadOnlyList<AccessDto>>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!caller.Permissions.CanShare)
        {
            return OperationResult<IReadOnlyList<AccessDto>>.Forbidden(
                ErrorCodes.NoPermissionManageSharing,
                "You do not have permission to manage sharing for this device.");
        }

        List<AccessDto> grants = await _context.Accesses
            .AsNoTracking()
            .Where(access => access.DeviceId == caller.DeviceRowId && access.IsActive)
            .OrderBy(access => access.DateRegistration)
            .Select(access => new AccessDto(
                access.Id,
                access.UserId,
                caller.DeviceId,
                access.GrantedBy,
                access.DateRegistration,
                access.CanRead,
                access.CanDelete,
                access.CanShare,
                access.CanModifySettings,
                access.IsOwner))
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<AccessDto>>.Success(grants);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AccessDto>> UpdateAsync(
        int userId,
        int accessId,
        AccessUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        GrantLookup lookup = await LoadGrantAsync(userId, accessId, cancellationToken);

        if (lookup.Failure is not null)
        {
            return OperationResult<AccessDto>.Failed(lookup.FailureOutcome, lookup.Failure);
        }

        Access grant = lookup.Grant!;
        DeviceAccessContext caller = lookup.Caller!;

        CapabilitySet capabilities = CapabilitySet.FromRequest(
            request.CanDelete,
            request.CanShare,
            request.CanModifySettings);

        ServiceError? refusal = GrantChangePolicy.CheckUpdate(Party(userId, caller), Party(grant), capabilities);

        if (refusal is not null)
        {
            return OperationResult<AccessDto>.Forbidden(refusal);
        }

        if (!capabilities.CanShare && grant.CanShare)
        {
            bool wouldOrphan = await IsLastSharerAsync(grant, cancellationToken);
            if (wouldOrphan)
            {
                return OperationResult<AccessDto>.Invalid(
                    ErrorCodes.LastSharer,
                    "This is the only account that can share this device. Give someone else sharing rights first.");
            }
        }

        // Losing CanShare takes the links made with it along. A demoted sharer keeps
        // seeing the device through their own grant, but may no longer hand that view
        // to strangers — and a link they made last week is exactly that. Read before
        // the flags below are overwritten, and saved in the same unit of work.
        bool losesSharing = grant.CanShare && !capabilities.CanShare;

        int linksRevoked = losesSharing
            ? await ShareLinkRevoker.RevokeCreatedByAsync(_context, grant.DeviceId, grant.UserId, cancellationToken)
            : 0;

        // Their open invitations go too — the same right issued them.
        if (losesSharing)
        {
            await AccessInvitationRemover.RemoveCreatedByAsync(_context, grant.DeviceId, grant.UserId, cancellationToken);
        }

        grant.CanDelete = capabilities.CanDelete;
        grant.CanShare = capabilities.CanShare;
        grant.CanModifySettings = capabilities.CanModifySettings;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} changed access {AccessId} on device {DeviceId}; {LinksRevoked} share link(s) revoked with it",
            userId,
            accessId,
            caller.DeviceId,
            linksRevoked);

        return OperationResult<AccessDto>.Success(ToDto(grant, caller.DeviceId));
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RevokeAsync(
        int userId,
        int accessId,
        CancellationToken cancellationToken)
    {
        GrantLookup lookup = await LoadGrantAsync(userId, accessId, cancellationToken);

        if (lookup.Failure is not null)
        {
            return OperationResult<bool>.Failed(lookup.FailureOutcome, lookup.Failure);
        }

        Access grant = lookup.Grant!;

        // Revoking only ever takes rights away, so it cannot escalate anything —
        // except against the owner, whose grant nobody removes.
        ServiceError? refusal = GrantChangePolicy.CheckRevoke(Party(grant));

        if (refusal is not null)
        {
            return OperationResult<bool>.Forbidden(refusal);
        }

        // What a revoke can still do is orphan the device's sharing.
        if (grant.CanShare && await IsLastSharerAsync(grant, cancellationToken))
        {
            return OperationResult<bool>.Invalid(
                ErrorCodes.LastSharer,
                "This is the only account that can share this device. Give someone else sharing rights first.");
        }

        // Soft revoke: the row drops out of the partial unique index (so access can
        // be granted again later) while the audit trail of who once had it survives.
        grant.IsActive = false;

        // And the share links this person made go with their access. Otherwise a
        // removed sharer would keep watching the device through any link they had
        // handed out — for up to Sharing:MaxWindowDays — after the owner believed
        // they were gone. Saved in the same unit of work as the revoke itself.
        int linksRevoked = await ShareLinkRevoker.RevokeCreatedByAsync(
            _context,
            grant.DeviceId,
            grant.UserId,
            cancellationToken);

        // Their open invitations go too — the same right issued them.
        await AccessInvitationRemover.RemoveCreatedByAsync(_context, grant.DeviceId, grant.UserId, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} revoked access {AccessId} on device {DeviceId}; {LinksRevoked} share link(s) revoked with it",
            userId,
            accessId,
            lookup.Caller!.DeviceId,
            linksRevoked);

        return OperationResult<bool>.Success(true);
    }

    /// <summary>
    /// Loads a grant by id and checks that the caller may administer it, resolving
    /// both in one place so the three mutating paths cannot drift apart.
    /// </summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="accessId">The grant being addressed.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The grant and the caller's context, or a populated failure.</returns>
    private async Task<GrantLookup> LoadGrantAsync(int userId, int accessId, CancellationToken cancellationToken)
    {
        Access? grant = await _context.Accesses
            .SingleOrDefaultAsync(access => access.Id == accessId && access.IsActive, cancellationToken);

        if (grant is null)
        {
            return GrantLookup.Failed(OperationOutcome.NotFound, ErrorCodes.NoSuchAccessGrant, "No such access grant.");
        }

        // The device id is looked up from the grant, then fed back through the
        // authorizer. Going the long way round is deliberate: it means the caller's
        // permission is established by exactly the same code path as everywhere
        // else, rather than by an ad-hoc query written here.
        string? deviceId = await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == grant.DeviceId)
            .Select(device => device.DeviceId)
            .SingleOrDefaultAsync(cancellationToken);

        if (deviceId is null)
        {
            return GrantLookup.Failed(OperationOutcome.NotFound, ErrorCodes.NoSuchAccessGrant, "No such access grant.");
        }

        DeviceAccessContext? caller = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (caller is null)
        {
            // The caller cannot see the device at all, so the grant on it is none of
            // their business — and saying "forbidden" would confirm it exists.
            return GrantLookup.Failed(OperationOutcome.NotFound, ErrorCodes.NoSuchAccessGrant, "No such access grant.");
        }

        if (!caller.Permissions.CanShare)
        {
            return GrantLookup.Failed(
                OperationOutcome.Forbidden,
                ErrorCodes.NoPermissionManageSharing,
                "You do not have permission to manage sharing for this device.");
        }

        return GrantLookup.Found(grant, caller);
    }

    /// <summary>
    /// Checks whether a grant is the only remaining one that can share its device.
    /// </summary>
    /// <param name="grant">The grant about to be revoked or demoted.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>True when removing its sharing right would leave the device unmanageable.</returns>
    private async Task<bool> IsLastSharerAsync(Access grant, CancellationToken cancellationToken)
    {
        int otherSharers = await _context.Accesses
            .AsNoTracking()
            .CountAsync(
                access => access.DeviceId == grant.DeviceId
                    && access.IsActive
                    && access.CanShare
                    && access.Id != grant.Id,
                cancellationToken);

        return otherSharers == 0;
    }

    /// <summary>Maps a grant entity onto its wire shape.</summary>
    /// <param name="grant">The stored grant.</param>
    /// <param name="deviceId">MQTT identity of the device it is on.</param>
    /// <returns>The DTO.</returns>
    private static AccessDto ToDto(Access grant, string deviceId)
    {
        return new AccessDto(
            grant.Id,
            grant.UserId,
            deviceId,
            grant.GrantedBy,
            grant.DateRegistration,
            grant.CanRead,
            grant.CanDelete,
            grant.CanShare,
            grant.CanModifySettings,
            grant.IsOwner);
    }

    /// <summary>The caller as the grant policy sees them.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="caller">Their resolved context on the device.</param>
    /// <returns>The policy's view of the caller.</returns>
    private static GrantParty Party(int userId, DeviceAccessContext caller)
    {
        return new GrantParty(userId, CapabilitySet.Of(caller.Permissions), caller.IsOwner);
    }

    /// <summary>A grant's holder as the grant policy sees them.</summary>
    /// <param name="grant">The stored grant.</param>
    /// <returns>The policy's view of the grantee.</returns>
    private static GrantParty Party(Access grant)
    {
        return new GrantParty(
            grant.UserId,
            new CapabilitySet(grant.CanDelete, grant.CanShare, grant.CanModifySettings),
            grant.IsOwner);
    }
}
