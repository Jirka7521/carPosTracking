using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Implements <see cref="IShareLinkService"/> — the creator's side of temporary
/// sharing.
///
/// <para>
/// It requires <c>CanShare</c> everywhere, resolved through the same
/// <see cref="IDeviceAccessAuthorizer"/> as every other device operation. That is
/// not merely consistency: a share link is a broader disclosure than an
/// <see cref="Access"/> grant, since the recipient needs no account and leaves no
/// identity behind, so it must not be reachable from any weaker permission.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class ShareLinkService : IShareLinkService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly IShareTokenFactory _tokens;
    private readonly IPassphraseGenerator _passphrases;
    private readonly IPasswordHasher _hasher;
    private readonly SharingOptions _options;
    private readonly ILogger<ShareLinkService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on a device.</param>
    /// <param name="tokens">Mints the link secret.</param>
    /// <param name="passphrases">Mints the visitor's code.</param>
    /// <param name="hasher">Hashes that code, with the same PBKDF2 used for passwords.</param>
    /// <param name="options">Window and per-device ceilings.</param>
    /// <param name="logger">Structured logger.</param>
    public ShareLinkService(
        CarPosDbContext context,
        IDeviceAccessAuthorizer authorizer,
        IShareTokenFactory tokens,
        IPassphraseGenerator passphrases,
        IPasswordHasher hasher,
        IOptions<SharingOptions> options,
        ILogger<ShareLinkService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _tokens = tokens;
        _passphrases = passphrases;
        _hasher = hasher;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ShareLinkDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? caller = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (caller is null)
        {
            return OperationResult<IReadOnlyList<ShareLinkDto>>.NotFound("No such device.");
        }

        if (!caller.Permissions.CanShare)
        {
            return OperationResult<IReadOnlyList<ShareLinkDto>>.Forbidden(
                "You do not have permission to manage sharing for this device.");
        }

        DateTime nowUtc = DateTime.UtcNow;

        // Projected without the three secret columns, so they are never read off the
        // page into memory at all — the same discipline the device queries apply to
        // the private-key ciphertext.
        List<ShareLinkRow> rows = await _context.ShareLinks
            .AsNoTracking()
            .Where(link => link.DeviceId == caller.DeviceRowId)
            .OrderByDescending(link => link.CreatedAt)
            .Select(link => new ShareLinkRow(
                link.Id,
                link.Label,
                link.ValidFrom,
                link.ValidUntil,
                link.Scope,
                link.IncludeSpeed,
                link.IncludeTelemetry,
                link.CreatedAt,
                link.RevokedAt,
                link.SuccessfulRedeems,
                link.LastAccessedAt,
                link.FailedAttempts,
                link.LockedUntil))
            .ToListAsync(cancellationToken);

        List<ShareLinkDto> links = new List<ShareLinkDto>(rows.Count);

        foreach (ShareLinkRow row in rows)
        {
            links.Add(ToDto(row, caller.DeviceId, nowUtc));
        }

        return OperationResult<IReadOnlyList<ShareLinkDto>>.Success(links);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ShareLinkCreatedDto>> CreateAsync(
        int userId,
        ShareLinkCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? caller = await _authorizer.ResolveAsync(userId, request.DeviceId, cancellationToken);

        if (caller is null)
        {
            return OperationResult<ShareLinkCreatedDto>.NotFound("No such device.");
        }

        if (!caller.Permissions.CanShare)
        {
            return OperationResult<ShareLinkCreatedDto>.Forbidden(
                "You do not have permission to share this device.");
        }

        if (!TryReadScope(request.Scope, out ShareScope scope))
        {
            return OperationResult<ShareLinkCreatedDto>.Invalid(
                "Choose whether the link shows the current position only or the whole track.");
        }

        DateTime nowUtc = DateTime.UtcNow;
        DateTime validFrom = NormaliseToUtc(request.ValidFrom);
        DateTime validUntil = NormaliseToUtc(request.ValidUntil);

        string? windowFailure = ValidateWindow(validFrom, validUntil, nowUtc);

        if (windowFailure is not null)
        {
            return OperationResult<ShareLinkCreatedDto>.Invalid(windowFailure);
        }

        // Counted against live links only. An expired link discloses nothing and a
        // revoked one less than that, so neither should stand in the way of sharing
        // again.
        int liveLinks = await _context.ShareLinks
            .AsNoTracking()
            .CountAsync(
                link => link.DeviceId == caller.DeviceRowId
                    && link.RevokedAt == null
                    && link.ValidUntil > nowUtc,
                cancellationToken);

        if (liveLinks >= _options.MaxLiveLinksPerDevice)
        {
            return OperationResult<ShareLinkCreatedDto>.Conflict(
                "This device already has as many active share links as are allowed. Revoke one before creating another.");
        }

        string label = await ResolveLabelAsync(request.Label, caller.DeviceRowId, cancellationToken);

        ShareToken token = _tokens.Create();
        string passphrase = _passphrases.Generate();

        ShareLink link = new ShareLink
        {
            Id = Guid.NewGuid(),
            Selector = token.Selector,
            VerifierHash = token.VerifierHash,
            // Normalised before hashing so the comparison at redeem time — which
            // normalises the visitor's typing the same way — compares like with like.
            // Hashing the hyphenated form here would make every correctly typed code
            // fail, and fail in the one place where failures look like an attack.
            PassphraseHash = _hasher.Hash(_passphrases.Normalise(passphrase)),
            DeviceId = caller.DeviceRowId,
            CreatedByUserId = userId,
            Label = label,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            Scope = scope,
            IncludeSpeed = request.IncludeSpeed,
            IncludeTelemetry = request.IncludeTelemetry,
            CreatedAt = nowUtc,
        };

        _context.ShareLinks.Add(link);
        await _context.SaveChangesAsync(cancellationToken);

        // Ids and a window only. The token and the code are the two values that must
        // never reach a log, and the device is named by its id rather than its label
        // because the label is the creator's free text.
        _logger.LogInformation(
            "User {UserId} created share link {ShareId} on device {DeviceId} valid {ValidFrom} to {ValidUntil}",
            userId,
            link.Id,
            caller.DeviceId,
            validFrom,
            validUntil);

        ShareLinkDto dto = ToDto(ShareLinkRow.From(link), caller.DeviceId, nowUtc);

        return OperationResult<ShareLinkCreatedDto>.Success(
            new ShareLinkCreatedDto(dto, token.Token, passphrase));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ShareLinkDto>> UpdateAsync(
        int userId,
        Guid shareId,
        ShareLinkUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ShareLinkLookup lookup = await LoadForManagementAsync(userId, shareId, cancellationToken);

        if (lookup.Failure is not null)
        {
            return new OperationResult<ShareLinkDto>(lookup.FailureOutcome, null, lookup.Failure);
        }

        ShareLink link = lookup.Link!;
        DeviceAccessContext caller = lookup.Caller!;

        // Revocation is terminal; expiry is not. The rule and the reasoning behind
        // it live in ShareLinkStatusResolver.IsEditable, where they are stated once
        // and pinned by a test.
        if (!ShareLinkStatusResolver.IsEditable(link.RevokedAt))
        {
            return OperationResult<ShareLinkDto>.Conflict(
                "This link has been revoked and cannot be changed. Create a new one instead.");
        }

        if (!TryReadScope(request.Scope, out ShareScope scope))
        {
            return OperationResult<ShareLinkDto>.Invalid(
                "Choose whether the link shows the current position only or the whole track.");
        }

        DateTime nowUtc = DateTime.UtcNow;
        DateTime validFrom = NormaliseToUtc(request.ValidFrom);
        DateTime validUntil = NormaliseToUtc(request.ValidUntil);

        string? windowFailure = ValidateWindow(validFrom, validUntil, nowUtc);

        if (windowFailure is not null)
        {
            return OperationResult<ShareLinkDto>.Invalid(windowFailure);
        }

        // Kept for the log line below: a window that moved is the disclosure-relevant
        // part of an edit, and worth being able to reconstruct afterwards.
        DateTime previousFrom = link.ValidFrom;
        DateTime previousUntil = link.ValidUntil;

        link.Label = await ResolveLabelAsync(request.Label, link.DeviceId, cancellationToken);
        link.ValidFrom = validFrom;
        link.ValidUntil = validUntil;
        link.Scope = scope;
        link.IncludeSpeed = request.IncludeSpeed;
        link.IncludeTelemetry = request.IncludeTelemetry;

        // Deliberately untouched: Selector, VerifierHash and PassphraseHash, so the
        // link and code already in somebody's hands keep working; CreatedAt and the
        // redeem counters, which are history rather than settings; and
        // FailedAttempts/LockedUntil — silently clearing a cooldown as a side effect
        // of renaming a label would turn an unrelated edit into a security decision.
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} updated share link {ShareId} on device {DeviceId}: window {PreviousFrom}-{PreviousUntil} is now {ValidFrom}-{ValidUntil}",
            userId,
            shareId,
            caller.DeviceId,
            previousFrom,
            previousUntil,
            validFrom,
            validUntil);

        return OperationResult<ShareLinkDto>.Success(
            ToDto(ShareLinkRow.From(link), caller.DeviceId, nowUtc));
    }

    /// <inheritdoc />
    public async Task<OperationResult<ShareLinkCreatedDto>> ReissueAsync(
        int userId,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        ShareLinkLookup lookup = await LoadForManagementAsync(userId, shareId, cancellationToken);

        if (lookup.Failure is not null)
        {
            return new OperationResult<ShareLinkCreatedDto>(lookup.FailureOutcome, null, lookup.Failure);
        }

        ShareLink link = lookup.Link!;
        DeviceAccessContext caller = lookup.Caller!;

        // Same terminal rule as editing: a withdrawal stays withdrawn. Reissuing a
        // revoked link would be a way to walk revocation back, which is exactly
        // what it must never be.
        if (!ShareLinkStatusResolver.IsEditable(link.RevokedAt))
        {
            return OperationResult<ShareLinkCreatedDto>.Conflict(
                "This link has been revoked. Create a new one instead.");
        }

        DateTime nowUtc = DateTime.UtcNow;

        ShareToken token = _tokens.Create();
        string passphrase = _passphrases.Generate();

        // Replacing the selector is what makes the old URL stop resolving — the
        // lookup it depends on simply finds nothing. The old code stops mattering
        // with it.
        link.Selector = token.Selector;
        link.VerifierHash = token.VerifierHash;
        link.PassphraseHash = _hasher.Hash(_passphrases.Normalise(passphrase));

        // Cleared, and this is the one place clearing them is right: the cooldown
        // accrued against a code that no longer exists. Carrying it over would lock
        // somebody out of a code they have not yet had a chance to type once.
        link.FailedAttempts = 0;
        link.LockedUntil = null;

        // Deliberately kept: the window, scope, flags and label — this reissues a
        // share rather than replacing it — and SuccessfulRedeems/LastAccessedAt,
        // which are history and would be a lie if reset.
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} reissued share link {ShareId} on device {DeviceId}; the previous link and code no longer work",
            userId,
            shareId,
            caller.DeviceId);

        ShareLinkDto dto = ToDto(ShareLinkRow.From(link), caller.DeviceId, nowUtc);

        return OperationResult<ShareLinkCreatedDto>.Success(
            new ShareLinkCreatedDto(dto, token.Token, passphrase));
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RevokeAsync(
        int userId,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        ShareLinkLookup lookup = await LoadForManagementAsync(userId, shareId, cancellationToken);

        if (lookup.Failure is not null)
        {
            return new OperationResult<bool>(lookup.FailureOutcome, false, lookup.Failure);
        }

        ShareLink link = lookup.Link!;

        // Revoking twice is not an error worth raising: the caller's intent is
        // already satisfied, and a 409 would only complicate a double-click on a
        // button whose whole purpose is to be pressed in a hurry.
        if (link.RevokedAt is null)
        {
            link.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "User {UserId} revoked share link {ShareId} on device {DeviceId}",
                userId,
                shareId,
                lookup.Caller!.DeviceId);
        }

        return OperationResult<bool>.Success(true);
    }

    /// <summary>
    /// Loads a link by id and checks that the caller may administer it, resolving
    /// both in one place so the two mutating paths cannot drift apart.
    /// </summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="shareId">The link being addressed.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The link and the caller's context, or a populated failure.</returns>
    private async Task<ShareLinkLookup> LoadForManagementAsync(
        int userId,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        // Tracked, because both callers mutate what comes back.
        ShareLink? link = await _context.ShareLinks
            .SingleOrDefaultAsync(candidate => candidate.Id == shareId, cancellationToken);

        if (link is null)
        {
            return ShareLinkLookup.Failed(OperationOutcome.NotFound, "No such share link.");
        }

        // The device is looked up from the link and fed back through the authorizer,
        // the long way round, exactly as AccessService.LoadGrantAsync does it: the
        // caller's permission is then established by the same code path as
        // everywhere else rather than by an ad-hoc query written here.
        string? deviceId = await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == link.DeviceId)
            .Select(device => device.DeviceId)
            .SingleOrDefaultAsync(cancellationToken);

        if (deviceId is null)
        {
            return ShareLinkLookup.Failed(OperationOutcome.NotFound, "No such share link.");
        }

        DeviceAccessContext? caller = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);

        if (caller is null)
        {
            // The caller cannot see the device, so a link on it is none of their
            // business — and answering "forbidden" would confirm that it exists.
            return ShareLinkLookup.Failed(OperationOutcome.NotFound, "No such share link.");
        }

        if (!caller.Permissions.CanShare)
        {
            return ShareLinkLookup.Failed(
                OperationOutcome.Forbidden,
                "You do not have permission to manage sharing for this device.");
        }

        return ShareLinkLookup.Found(link, caller);
    }

    /// <summary>
    /// Checks a requested window against the rules that keep a share temporary.
    ///
    /// Returns the message rather than a typed result so that create and update,
    /// which produce differently-typed results, can enforce one set of rules. An
    /// edit is held to exactly the same limits as a creation — otherwise the
    /// ceiling on a window would be avoidable by creating a short link and then
    /// stretching it.
    /// </summary>
    /// <param name="validFrom">Start of the window (UTC).</param>
    /// <param name="validUntil">End of the window (UTC).</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>A message for the caller, or null when the window is acceptable.</returns>
    private string? ValidateWindow(DateTime validFrom, DateTime validUntil, DateTime nowUtc)
    {
        if (validUntil <= validFrom)
        {
            return "The end of the sharing window must be after its start.";
        }

        if (validUntil <= nowUtc)
        {
            return "That sharing window has already passed.";
        }

        // Measured from the start rather than from now, so a window scheduled for
        // next month is bounded by its own length and not by how far off it is.
        if (validUntil - validFrom > TimeSpan.FromDays(_options.MaxWindowDays))
        {
            return "That sharing window is longer than a share link is allowed to cover.";
        }

        return null;
    }

    /// <summary>
    /// Settles what the visitor will see the tracker called: the creator's label if
    /// they set one, otherwise the device's display name.
    /// </summary>
    /// <param name="requested">The label from the request, possibly blank.</param>
    /// <param name="deviceRowId">Internal id of the device being shared.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>A non-empty label.</returns>
    private async Task<string> ResolveLabelAsync(
        string? requested,
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim();
        }

        string? displayName = await _context.Devices
            .AsNoTracking()
            .Where(device => device.Id == deviceRowId)
            .Select(device => device.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);

        // The column is required, so this only falls through if the device vanished
        // between the authorizer's read and this one. An empty label would still be a
        // valid share; a generic word is a kinder thing to show a visitor.
        return string.IsNullOrWhiteSpace(displayName) ? "Tracker" : displayName;
    }

    /// <summary>Maps a wire scope name onto the stored enum.</summary>
    /// <param name="value">The name from the request.</param>
    /// <param name="scope">The parsed scope, when recognised.</param>
    /// <returns>
    /// True when the name is one this API publishes. An unrecognised name is refused
    /// rather than defaulted: silently widening a share because a client sent a typo
    /// is the wrong way to be forgiving.
    /// </returns>
    private static bool TryReadScope(string value, out ShareScope scope)
    {
        switch (value)
        {
            case ShareScopeNames.LatestOnly:
                scope = ShareScope.LatestOnly;
                return true;

            case ShareScopeNames.FullTrack:
                scope = ShareScope.FullTrack;
                return true;

            default:
                scope = ShareScope.LatestOnly;
                return false;
        }
    }

    /// <summary>Maps a projected row onto its wire shape, deriving the status.</summary>
    /// <param name="row">The columns read from the database.</param>
    /// <param name="deviceId">MQTT identity of the device it shares.</param>
    /// <param name="nowUtc">The instant the status is judged against.</param>
    /// <returns>The DTO.</returns>
    private static ShareLinkDto ToDto(ShareLinkRow row, string deviceId, DateTime nowUtc)
    {
        return new ShareLinkDto(
            row.Id,
            deviceId,
            row.Label,
            row.ValidFrom,
            row.ValidUntil,
            row.Scope == ShareScope.FullTrack ? ShareScopeNames.FullTrack : ShareScopeNames.LatestOnly,
            row.IncludeSpeed,
            row.IncludeTelemetry,
            ShareLinkStatusResolver.Resolve(row.RevokedAt, row.ValidFrom, row.ValidUntil, row.LockedUntil, nowUtc),
            row.CreatedAt,
            row.RevokedAt,
            row.SuccessfulRedeems,
            row.LastAccessedAt,
            row.FailedAttempts,
            row.LockedUntil);
    }

    /// <summary>
    /// Forces a window bound to <see cref="DateTimeKind.Utc"/>.
    ///
    /// Same reasoning as <c>PositionQueryService.NormaliseToUtc</c>: these columns
    /// are <c>timestamptz</c> and Npgsql refuses a parameter of any other kind, so a
    /// bound arriving as Local or Unspecified would throw at execution rather than
    /// store. Local values are converted, not re-labelled.
    /// </summary>
    /// <param name="value">A bound as model-bound from the request body.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>.</returns>
    private static DateTime NormaliseToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
