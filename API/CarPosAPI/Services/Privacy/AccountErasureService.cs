using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Common;
using CarPosAPI.Services.Ingest;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Privacy;

/// <summary>
/// Implements <see cref="IAccountErasureService"/>.
///
/// The order below is not arbitrary — every foreign key pointing at a user or a
/// device is <c>DeleteBehavior.Restrict</c>, so anything referencing a row has to
/// go, or be unlinked, before that row can. The whole sequence runs in one
/// transaction: a half-erased account is worse than either outcome, because the
/// user has been told their data is gone while some of it demonstrably is not.
///
/// <para>
/// Devices are the interesting case. A device only this account could see is
/// deleted outright, history and all. A device somebody else still has an active
/// grant on is left completely alone — erasing one person must not destroy
/// another person's data — and only this account's own grant and nickname go.
/// </para>
///
/// Scoped: it holds the request's <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class AccountErasureService : IAccountErasureService
{
    private readonly CarPosDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfigPublisher _configPublisher;
    private readonly ILogger<AccountErasureService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="passwordHasher">Verifies the caller's password before anything is destroyed.</param>
    /// <param name="configPublisher">Clears retained broker messages for deleted devices.</param>
    /// <param name="logger">Structured logger — ids and counts only, never data.</param>
    public AccountErasureService(
        CarPosDbContext context,
        IPasswordHasher passwordHasher,
        IConfigPublisher configPublisher,
        ILogger<AccountErasureService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _configPublisher = configPublisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<AccountErasureSummary>> EraseAsync(
        int userId,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);

        User? user = await _context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return OperationResult<AccountErasureSummary>.NotFound("No such user.");
        }

        // Proof of identity before an irreversible act. A stolen session cookie is
        // enough to read this account's data; it must not also be enough to destroy
        // it, which would turn a session theft into a denial-of-service against the
        // person whose data it is.
        if (_passwordHasher.Check(user.PasswordHash, password) == PasswordCheckResult.Failed)
        {
            _logger.LogInformation(
                "Account erasure refused for user {UserId}: password did not match",
                userId);
            return OperationResult<AccountErasureSummary>.Invalid("Your password is not correct.");
        }

        // Worked out before the transaction opens, so the broker cleanup afterwards
        // knows which identities to clear even though their rows will be gone.
        List<Guid> solelyOwnedDeviceRowIds = await FindSolelyVisibleDeviceRowIdsAsync(userId, cancellationToken);

        List<string> solelyOwnedDeviceIds = await _context.Devices
            .AsNoTracking()
            .Where(device => solelyOwnedDeviceRowIds.Contains(device.Id))
            .Select(device => device.DeviceId)
            .ToListAsync(cancellationToken);

        int retainedDevices = await _context.Accesses
            .AsNoTracking()
            .Where(access => access.UserId == userId && access.IsActive)
            .Select(access => access.DeviceId)
            .Distinct()
            .CountAsync(cancellationToken) - solelyOwnedDeviceRowIds.Count;

        AccountErasureSummary summary = await EraseInTransactionAsync(
            userId,
            solelyOwnedDeviceRowIds,
            retainedDevices < 0 ? 0 : retainedDevices,
            cancellationToken);

        // Only after the rows are committed gone. A retained message on the broker
        // outlives the database row it came from, so a device's settings and its
        // weekly tracking pattern would otherwise sit there indefinitely.
        foreach (string deviceId in solelyOwnedDeviceIds)
        {
            await _configPublisher.ClearRetainedAsync(deviceId, cancellationToken);
        }

        _logger.LogInformation(
            "Erased account {UserId}: {DevicesDeleted} device(s) and {PositionsDeleted} position(s) deleted, "
            + "{DevicesRetained} device(s) retained for other users, {GrantsAnonymised} grant(s) anonymised",
            userId,
            summary.DevicesDeleted,
            summary.PositionsDeleted,
            summary.DevicesRetained,
            summary.GrantsAnonymised);

        return OperationResult<AccountErasureSummary>.Success(summary);
    }

    /// <summary>
    /// Finds the devices that would become invisible to everybody once this account
    /// is gone — the ones this user is the last party to, and nobody else can reach.
    /// Those are the devices whose history is erased with the account.
    /// <para>
    /// The user&apos;s own grants are matched <em>regardless</em> of
    /// <see cref="Data.Entities.Access.IsActive"/>, and that asymmetry against the
    /// <c>other</c> clause below is the point. A device whose only remaining grant is
    /// this user&apos;s <em>revoked</em> one is reachable by nobody at all; treating it as
    /// not-mine left the row and its entire position history in the database forever,
    /// while the erasure went on to delete the revoked grant and with it the last
    /// trace of whose device it had been. Data nobody can reach and nobody deletes is
    /// precisely what Art. 17 is about, so a revoked grant still counts as a reason to
    /// clean the device up.
    /// </para>
    /// </summary>
    /// <param name="userId">The account being erased.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Internal device ids, possibly empty.</returns>
    private async Task<List<Guid>> FindSolelyVisibleDeviceRowIdsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return await _context.Accesses
            .AsNoTracking()
            .Where(mine => mine.UserId == userId)
            .Select(mine => mine.DeviceId)
            .Distinct()
            .Where(deviceRowId => !_context.Accesses.Any(other =>
                other.DeviceId == deviceRowId
                && other.IsActive
                && other.UserId != userId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Performs the deletion itself, in one transaction.
    ///
    /// Ordered by the foreign keys: everything that points at a row is removed or
    /// unlinked before the row it points at. <c>ExecuteDeleteAsync</c> throughout,
    /// because loading a year of positions into the change tracker only to delete
    /// them would be an outage dressed up as a privacy feature.
    /// </summary>
    /// <param name="userId">The account being erased.</param>
    /// <param name="deviceRowIds">Devices to delete outright.</param>
    /// <param name="retainedDevices">How many devices are being left for other users.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was removed.</returns>
    private async Task<AccountErasureSummary> EraseInTransactionAsync(
        int userId,
        List<Guid> deviceRowIds,
        int retainedDevices,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        // 1. This account's own nicknames and grants. Both are purely personal —
        //    nobody else has any interest in them.
        await _context.DeviceAliases
            .Where(alias => alias.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        int grantsDeleted = await _context.Accesses
            .Where(access => access.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        // 2. Grants this account handed to OTHER people. These must survive — the
        //    other user still has access, and revoking it would be erasing somebody
        //    else's data on this person's behalf. Nulling the reference removes the
        //    personal link while leaving the grant standing.
        int grantsAnonymised = await _context.Accesses
            .Where(access => access.GrantedBy == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(access => access.GrantedBy, (int?)null),
                cancellationToken);

        // 3. The same treatment for the configuration audit trail, which is kept
        //    indefinitely: what changed survives, who changed it does not.
        await _context.DeviceConfigVersions
            .Where(revision => revision.CreatedByUserId == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(revision => revision.CreatedByUserId, (int?)null),
                cancellationToken);

        await _context.DeviceConfigProfiles
            .Where(profile => profile.CreatedByUserId == userId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(profile => profile.CreatedByUserId, (int?)null),
                cancellationToken);

        (int devicesDeleted, long positionsDeleted) =
            await DeleteDevicesAsync(deviceRowIds, cancellationToken);

        // 4. Last, now that nothing references it.
        await _context.Users
            .Where(candidate => candidate.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new AccountErasureSummary(
            devicesDeleted,
            retainedDevices,
            positionsDeleted,
            grantsDeleted,
            grantsAnonymised);
    }

    /// <summary>
    /// Deletes the given devices outright, with everything that hangs off them.
    ///
    /// Positions first, because <c>positions.device_id</c> is a <c>Restrict</c>
    /// foreign key and the device row cannot go while a single fix still points at
    /// it. Schedule rules before profiles for the same reason, and both before the
    /// device — even though the device would cascade to them — so the counts are
    /// honest and the order does not depend on a cascade rule staying as it is.
    /// </summary>
    /// <param name="deviceRowIds">Internal ids of the devices to delete.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many devices and how many position rows went.</returns>
    private async Task<(int DevicesDeleted, long PositionsDeleted)> DeleteDevicesAsync(
        List<Guid> deviceRowIds,
        CancellationToken cancellationToken)
    {
        if (deviceRowIds.Count == 0)
        {
            return (0, 0);
        }

        long positionsDeleted = await _context.Positions
            .Where(position => deviceRowIds.Contains(position.DeviceId))
            .ExecuteDeleteAsync(cancellationToken);

        await _context.DeviceConfigScheduleRules
            .Where(rule => deviceRowIds.Contains(rule.DeviceId))
            .ExecuteDeleteAsync(cancellationToken);

        // The fallback reference points at a profile row, so it has to let go before
        // the profiles can be deleted.
        await _context.Devices
            .Where(device => deviceRowIds.Contains(device.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(device => device.ConfigScheduleFallbackProfileId, (Guid?)null),
                cancellationToken);

        await _context.DeviceConfigProfiles
            .Where(profile => deviceRowIds.Contains(profile.DeviceId))
            .ExecuteDeleteAsync(cancellationToken);

        await _context.DeviceConfigVersions
            .Where(revision => deviceRowIds.Contains(revision.DeviceId))
            .ExecuteDeleteAsync(cancellationToken);

        // Any remaining grant on these devices is an inactive one — an active grant
        // held by somebody else is exactly what kept a device off this list. Revoked
        // rows have no one left to be an audit trail for.
        await _context.Accesses
            .Where(access => deviceRowIds.Contains(access.DeviceId))
            .ExecuteDeleteAsync(cancellationToken);

        await _context.DeviceAliases
            .Where(alias => deviceRowIds.Contains(alias.DeviceId))
            .ExecuteDeleteAsync(cancellationToken);

        int devicesDeleted = await _context.Devices
            .Where(device => deviceRowIds.Contains(device.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return (devicesDeleted, positionsDeleted);
    }
}
