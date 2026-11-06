using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Implements <see cref="IAccessInvitationService"/>.
///
/// <para>
/// <b>Consent is the point.</b> A grant used to be created for any account the sharer
/// named, on the spot. Since a shared device is what makes two accounts visible to
/// each other, one side alone could create that relationship with anybody and read
/// their name and email. Here a grant exists only once its recipient has seen the
/// offer and accepted it, and the offer is a link, not a name — making one reveals
/// nothing about who has an account.
/// </para>
///
/// <para>
/// <b>Nothing is trusted from the moment the link was made.</b> Acceptance re-runs
/// every check against the sharer's access as it is now: the device must still be
/// active, the sharer must still hold <c>CanShare</c>, and
/// <see cref="GrantChangePolicy.CheckCreate"/> must still allow every capability
/// offered. An invitation therefore never outlives the right that issued it.
/// </para>
///
/// <para>
/// <b>Single use</b> is enforced by the database rather than by a flag: the invitation
/// row is deleted in the same <c>SaveChanges</c> that creates the grant, and EF fails
/// a delete that affects no row, so two people racing with one link cannot both win.
/// Tokens follow the share-link scheme (<see cref="IShareTokenFactory"/>): only the
/// verifier's digest is stored, and every failure to use one — unknown, malformed,
/// expired, used, withdrawn — gets the same answer.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class AccessInvitationService : IAccessInvitationService
{
    /// <summary>The one answer for every link that cannot be used, whatever the reason.</summary>
    private static readonly ServiceError InvalidLink = new ServiceError(
        ErrorCodes.InvitationInvalid,
        "This invitation link is not valid. It may have expired, been used already or been withdrawn — ask for a new one.");

    /// <summary>The link is genuine, but whoever made it can no longer give this access.</summary>
    private static readonly ServiceError NoLongerValid = new ServiceError(
        ErrorCodes.InvitationNoLongerValid,
        "Whoever made this invitation can no longer share this device in this way, so it cannot be accepted. Ask them for a new one.");

    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly IShareTokenFactory _tokens;
    private readonly SharingOptions _options;
    private readonly ILogger<AccessInvitationService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves an account's grant on a device.</param>
    /// <param name="tokens">Mints, parses and verifies link tokens (the share-link scheme).</param>
    /// <param name="options">Invitation lifetime and the per-device cap.</param>
    /// <param name="logger">Structured logger — never receives a token.</param>
    public AccessInvitationService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        IShareTokenFactory tokens,
        IOptions<SharingOptions> options,
        ILogger<AccessInvitationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _authorizer = authorizer;
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<AccessInvitationCreatedDto>> CreateAsync(
        int userId,
        AccessInvitationCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? caller = await _authorizer.ResolveAsync(userId, request.DeviceId, cancellationToken);

        if (caller is null)
        {
            return OperationResult<AccessInvitationCreatedDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!caller.Permissions.CanShare)
        {
            return OperationResult<AccessInvitationCreatedDto>.Forbidden(
                ErrorCodes.NoPermissionShare,
                "You do not have permission to share this device.");
        }

        if (!caller.IsActive)
        {
            return OperationResult<AccessInvitationCreatedDto>.Invalid(
                ErrorCodes.InvitationDeviceDeleted,
                "This device has been deleted, so nobody new can be invited to it.");
        }

        // The same coercion and the same escalation rules as editing a grant: an
        // invitation may offer only what its maker could hand out directly.
        CapabilitySet capabilities = CapabilitySet.FromRequest(
            request.CanDelete,
            request.CanShare,
            request.CanModifySettings);

        ServiceError? refusal = GrantChangePolicy.CheckCreate(Party(userId, caller), capabilities);

        if (refusal is not null)
        {
            return OperationResult<AccessInvitationCreatedDto>.Forbidden(refusal);
        }

        DateTime nowUtc = DateTime.UtcNow;

        // Housekeeping on the write path, so the table needs no job of its own: an
        // expired invitation can never be accepted and is only clutter.
        await _context.AccessInvitations
            .Where(invitation => invitation.DeviceId == caller.DeviceRowId && invitation.ExpiresAt <= nowUtc)
            .ExecuteDeleteAsync(cancellationToken);

        int pending = await _context.AccessInvitations
            .AsNoTracking()
            .CountAsync(invitation => invitation.DeviceId == caller.DeviceRowId, cancellationToken);

        if (pending >= _options.MaxPendingInvitationsPerDevice)
        {
            return OperationResult<AccessInvitationCreatedDto>.Conflict(
                ErrorCodes.TooManyInvitations,
                $"This device already has {_options.MaxPendingInvitationsPerDevice} open invitations. Withdraw one before making another.",
                new Dictionary<string, object> { ["max"] = _options.MaxPendingInvitationsPerDevice });
        }

        ShareToken token = _tokens.Create();

        AccessInvitation created = new AccessInvitation
        {
            Id = Guid.NewGuid(),
            Selector = token.Selector,
            // The digest only. The token goes back to the caller once, in this
            // response, and exists nowhere else.
            VerifierHash = ShareSecretHash.Of(token.Verifier),
            DeviceId = caller.DeviceRowId,
            CreatedByUserId = userId,
            CanDelete = capabilities.CanDelete,
            CanShare = capabilities.CanShare,
            CanModifySettings = capabilities.CanModifySettings,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.AddDays(_options.InvitationLifetimeDays),
        };

        _context.AccessInvitations.Add(created);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} made invitation {InvitationId} to device {DeviceId}",
            userId,
            created.Id,
            caller.DeviceId);

        return OperationResult<AccessInvitationCreatedDto>.Success(
            new AccessInvitationCreatedDto(ToDto(created, caller.DeviceId), token.Token));
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<AccessInvitationDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? caller = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (caller is null)
        {
            return OperationResult<IReadOnlyList<AccessInvitationDto>>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!caller.Permissions.CanShare)
        {
            return OperationResult<IReadOnlyList<AccessInvitationDto>>.Forbidden(
                ErrorCodes.NoPermissionManageSharing,
                "You do not have permission to manage sharing for this device.");
        }

        DateTime nowUtc = DateTime.UtcNow;

        List<AccessInvitationDto> invitations = await _context.AccessInvitations
            .AsNoTracking()
            .Where(invitation => invitation.DeviceId == caller.DeviceRowId && invitation.ExpiresAt > nowUtc)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .Select(invitation => new AccessInvitationDto(
                invitation.Id,
                caller.DeviceId,
                invitation.CreatedByUserId,
                invitation.CanDelete,
                invitation.CanShare,
                invitation.CanModifySettings,
                invitation.CreatedAt,
                invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<AccessInvitationDto>>.Success(invitations);
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> WithdrawAsync(
        int userId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        AccessInvitation? invitation = await _context.AccessInvitations
            .SingleOrDefaultAsync(candidate => candidate.Id == invitationId, cancellationToken);

        // The device is looked up from the invitation and fed back through the
        // authorizer, as AccessService.LoadGrantAsync does for grants: the caller's
        // permission is established by the same code path as everywhere else. An
        // invitation on a device the caller cannot see is "no such invitation" — a
        // 403 would confirm it exists.
        string? deviceId = invitation is null ? null : await DeviceIdOfAsync(invitation.DeviceId, cancellationToken);
        DeviceAccessContext? caller = deviceId is null
            ? null
            : await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (invitation is null || caller is null)
        {
            return OperationResult<bool>.NotFound(ErrorCodes.NoSuchInvitation, "No such invitation.");
        }

        if (!caller.Permissions.CanShare)
        {
            return OperationResult<bool>.Forbidden(
                ErrorCodes.NoPermissionManageSharing,
                "You do not have permission to manage sharing for this device.");
        }

        _context.AccessInvitations.Remove(invitation);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Accepted, declined or withdrawn by someone else a moment ago. Either way
            // the link no longer works, which is what was asked for.
        }

        _logger.LogInformation(
            "User {UserId} withdrew invitation {InvitationId} to device {DeviceId}",
            userId,
            invitationId,
            caller.DeviceId);

        return OperationResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AccessInvitationPreviewDto>> PreviewAsync(
        int userId,
        string token,
        CancellationToken cancellationToken)
    {
        AccessInvitation? invitation = await FindUsableAsync(token, cancellationToken);

        if (invitation is null)
        {
            return OperationResult<AccessInvitationPreviewDto>.Failed(OperationOutcome.NotFound, InvalidLink);
        }

        DeviceAccessContext? inviter = await InviterStillEntitledAsync(invitation, cancellationToken);

        UserProfileDto? inviterProfile = inviter is null
            ? null
            : await _context.Users
                .AsNoTracking()
                .Where(user => user.Id == invitation.CreatedByUserId)
                .Select(user => new UserProfileDto(user.Id, user.Email, user.FirstName, user.LastName))
                .SingleOrDefaultAsync(cancellationToken);

        if (inviter is null || inviterProfile is null)
        {
            return OperationResult<AccessInvitationPreviewDto>.Invalid(NoLongerValid);
        }

        string? displayName = await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == invitation.DeviceId)
            .Select(device => device.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);

        bool alreadyHasAccess =
            await _authorizer.ResolveAsync(userId, inviter.DeviceId, cancellationToken) is not null;

        // The inviter's name and nothing else about them — not the email address,
        // which the link holder has no need of to decide.
        return OperationResult<AccessInvitationPreviewDto>.Success(new AccessInvitationPreviewDto(
            inviter.DeviceId,
            displayName,
            inviterProfile.FirstName,
            inviterProfile.LastName,
            invitation.CanDelete,
            invitation.CanShare,
            invitation.CanModifySettings,
            invitation.ExpiresAt,
            alreadyHasAccess));
    }

    /// <inheritdoc />
    public async Task<OperationResult<AccessDto>> AcceptAsync(
        int userId,
        string token,
        CancellationToken cancellationToken)
    {
        AccessInvitation? invitation = await FindUsableAsync(token, cancellationToken);

        if (invitation is null)
        {
            return OperationResult<AccessDto>.Failed(OperationOutcome.NotFound, InvalidLink);
        }

        if (invitation.CreatedByUserId == userId)
        {
            return OperationResult<AccessDto>.Invalid(
                ErrorCodes.CannotAcceptOwnInvitation,
                "You made this invitation yourself. Send the link to the person you want to share the device with.");
        }

        DeviceAccessContext? inviter = await InviterStillEntitledAsync(invitation, cancellationToken);

        if (inviter is null)
        {
            return OperationResult<AccessDto>.Invalid(NoLongerValid);
        }

        Access? existing = await _context.Accesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                access => access.UserId == userId && access.DeviceId == invitation.DeviceId && access.IsActive,
                cancellationToken);

        if (existing is not null)
        {
            // The invitation is left alone: it was most likely meant for somebody else.
            return OperationResult<AccessDto>.Conflict(
                ErrorCodes.AccessExists,
                "You already have access to this device. Your existing access stays as it is.");
        }

        // A previously revoked grant is reactivated rather than duplicated. The
        // partial unique index only covers active rows, so inserting would succeed
        // and leave two rows for one pair — one of which the authorizer's
        // SingleOrDefault would then choke on.
        Access? revoked = await _context.Accesses
            .Where(access => access.UserId == userId && access.DeviceId == invitation.DeviceId && !access.IsActive)
            .OrderByDescending(access => access.DateRegistration)
            .FirstOrDefaultAsync(cancellationToken);

        Access grant;

        if (revoked is not null)
        {
            revoked.IsActive = true;
            revoked.GrantedBy = invitation.CreatedByUserId;
            revoked.DateRegistration = DateTime.UtcNow;
            revoked.CanRead = true;
            revoked.CanDelete = invitation.CanDelete;
            revoked.CanShare = invitation.CanShare;
            revoked.CanModifySettings = invitation.CanModifySettings;
            grant = revoked;
        }
        else
        {
            grant = new Access
            {
                UserId = userId,
                DeviceId = invitation.DeviceId,
                GrantedBy = invitation.CreatedByUserId,
                CanRead = true,
                CanDelete = invitation.CanDelete,
                CanShare = invitation.CanShare,
                CanModifySettings = invitation.CanModifySettings,
                IsActive = true,
                DateRegistration = DateTime.UtcNow,
            };

            _context.Accesses.Add(grant);
        }

        // Spent in the same unit of work as the grant it produced. If another
        // acceptance got there first, this delete affects no row, EF throws, and
        // nothing of this one is written.
        _context.AccessInvitations.Remove(invitation);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<AccessDto>.Failed(OperationOutcome.NotFound, InvalidLink);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // A double-click: the first click's grant is already active.
            return OperationResult<AccessDto>.Conflict(
                ErrorCodes.AccessExists,
                "You already have access to this device. Your existing access stays as it is.");
        }

        _logger.LogInformation(
            "User {UserId} accepted invitation {InvitationId} from user {InviterId} to device {DeviceId}",
            userId,
            invitation.Id,
            invitation.CreatedByUserId,
            inviter.DeviceId);

        return OperationResult<AccessDto>.Success(ToDto(grant, inviter.DeviceId));
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DeclineAsync(
        int userId,
        string token,
        CancellationToken cancellationToken)
    {
        AccessInvitation? invitation = await FindUsableAsync(token, cancellationToken);

        if (invitation is null)
        {
            return OperationResult<bool>.Failed(OperationOutcome.NotFound, InvalidLink);
        }

        _context.AccessInvitations.Remove(invitation);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else accepted it a moment ago — say so rather than claim a
            // decline that did not happen.
            return OperationResult<bool>.Failed(OperationOutcome.NotFound, InvalidLink);
        }

        _logger.LogInformation(
            "User {UserId} declined invitation {InvitationId}",
            userId,
            invitation.Id);

        return OperationResult<bool>.Success(true);
    }

    /// <summary>
    /// Finds the invitation a token opens, if it can still be used. Tracked, because
    /// accepting and declining delete it.
    /// </summary>
    /// <param name="token">The token as presented.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The invitation, or null for any token that does not open a live one.</returns>
    private async Task<AccessInvitation?> FindUsableAsync(string token, CancellationToken cancellationToken)
    {
        // Shape first, so a malformed or oversized token never becomes a query.
        if (!_tokens.TryParse(token, out string selector, out string verifier))
        {
            return null;
        }

        AccessInvitation? invitation = await _context.AccessInvitations
            .SingleOrDefaultAsync(candidate => candidate.Selector == selector, cancellationToken);

        if (invitation is null || !_tokens.VerifierMatches(invitation.VerifierHash, verifier))
        {
            return null;
        }

        return invitation.ExpiresAt > DateTime.UtcNow ? invitation : null;
    }

    /// <summary>
    /// Re-checks, now, that the invitation's maker could still give what it offers.
    /// </summary>
    /// <param name="invitation">The invitation being previewed or accepted.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The maker's current context on the device, or null when the device is gone or
    /// deleted, the maker has lost <c>CanShare</c>, or they no longer hold every
    /// capability the invitation offers.
    /// </returns>
    private async Task<DeviceAccessContext?> InviterStillEntitledAsync(
        AccessInvitation invitation,
        CancellationToken cancellationToken)
    {
        string? deviceId = await DeviceIdOfAsync(invitation.DeviceId, cancellationToken);

        if (deviceId is null)
        {
            return null;
        }

        DeviceAccessContext? inviter =
            await _authorizer.ResolveAsync(invitation.CreatedByUserId, deviceId, cancellationToken);

        if (inviter is null || !inviter.IsActive || !inviter.Permissions.CanShare)
        {
            return null;
        }

        CapabilitySet offered = new CapabilitySet(
            invitation.CanDelete,
            invitation.CanShare,
            invitation.CanModifySettings);

        return GrantChangePolicy.CheckCreate(Party(invitation.CreatedByUserId, inviter), offered) is null
            ? inviter
            : null;
    }

    /// <summary>The MQTT identity of a device, by its row id; never materialises the row.</summary>
    /// <param name="deviceRowId">Internal row id.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The device id, or null when the row does not exist.</returns>
    private Task<string?> DeviceIdOfAsync(Guid deviceRowId, CancellationToken cancellationToken)
    {
        return _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == deviceRowId)
            .Select(device => (string?)device.DeviceId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Maps an invitation onto its sharing-list shape.</summary>
    /// <param name="invitation">The stored invitation.</param>
    /// <param name="deviceId">MQTT identity of its device.</param>
    /// <returns>The DTO — with no part of the token.</returns>
    private static AccessInvitationDto ToDto(AccessInvitation invitation, string deviceId)
    {
        return new AccessInvitationDto(
            invitation.Id,
            deviceId,
            invitation.CreatedByUserId,
            invitation.CanDelete,
            invitation.CanShare,
            invitation.CanModifySettings,
            invitation.CreatedAt,
            invitation.ExpiresAt);
    }

    /// <summary>Maps a grant entity onto its wire shape, as AccessService does.</summary>
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

    /// <summary>An account as the grant policy sees it.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="context">Its resolved context on the device.</param>
    /// <returns>The policy's view of it.</returns>
    private static GrantParty Party(int userId, DeviceAccessContext context)
    {
        return new GrantParty(userId, CapabilitySet.Of(context.Permissions), context.IsOwner);
    }
}
