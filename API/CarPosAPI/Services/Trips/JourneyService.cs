using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Authorization;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="IJourneyService"/>.
///
/// <para>
/// <b>Rules that span rows</b> — nesting depth, cycles, sibling names, ranges that may
/// only be disjoint or nested, the per-device cap — are checked by
/// <see cref="JourneyTreeRules"/> against the device's journeys loaded inside the write
/// transaction, after taking a per-device advisory lock. Without the lock two saves
/// could each pass the overlap check against a picture that excluded the other.
/// </para>
///
/// <para>
/// <b>Saving a range never rewrites trips</b>, because range membership is computed at
/// query time (<see cref="TripQueries"/>). The one exception is the caller's explicit
/// "also move trips I placed by hand": those trips in the range are unpinned, so the new
/// range takes them.
/// </para>
///
/// <para>
/// <b>Deleting moves everything up one level</b>: sub-journeys to the parent, and the
/// journey's trips pinned to the parent — or, at the top level, back to the date ranges.
/// No trip is ever deleted here.
/// </para>
///
/// Scoped.
/// </summary>
internal sealed class JourneyService : IJourneyService
{
    private readonly CarPosDbContext _context;
    private readonly IDeviceAccessAuthorizer _authorizer;
    private readonly ILogger<JourneyService> _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    /// <param name="authorizer">Resolves the caller's grant on the device.</param>
    /// <param name="logger">Structured logger — ids and counts only, never names.</param>
    public JourneyService(CarPosDbContext context, IDeviceAccessAuthorizer authorizer, ILogger<JourneyService> logger)
    {
        _context = context;
        _authorizer = authorizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<JourneyListDto>> ListAsync(int userId, string deviceId, CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<JourneyListDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        return OperationResult<JourneyListDto>.Success(await BuildListAsync(access.DeviceRowId, cancellationToken));
    }

    /// <inheritdoc />
    public Task<OperationResult<JourneyDto>> CreateAsync(
        int userId,
        string deviceId,
        JourneySaveRequestDto request,
        CancellationToken cancellationToken) =>
        SaveAsync(userId, deviceId, null, request, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<JourneyDto>> UpdateAsync(
        int userId,
        string deviceId,
        Guid journeyId,
        JourneySaveRequestDto request,
        CancellationToken cancellationToken) =>
        SaveAsync(userId, deviceId, journeyId, request, cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DeleteAsync(
        int userId,
        string deviceId,
        Guid journeyId,
        CancellationToken cancellationToken)
    {
        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<bool>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return Forbidden<bool>();
        }

        Guid deviceRowId = access.DeviceRowId;
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        OperationResult<bool> result = await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(attemptToken);
                await LockDeviceJourneysAsync(deviceRowId, attemptToken);

                List<JourneySnapshot> all = await LoadSnapshotsAsync(deviceRowId, attemptToken);
                JourneySnapshot? target = all.SingleOrDefault(journey => journey.Id == journeyId);
                if (target is null)
                {
                    return OperationResult<bool>.NotFound(ErrorCodes.NoSuchJourney, "No such journey.");
                }

                // The children are about to join their grandparent's children; a name clash
                // there would break the sibling-name rule.
                HashSet<string> namesAtParent = all
                    .Where(journey => journey.ParentId == target.ParentId && journey.Id != target.Id)
                    .Select(journey => journey.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                JourneySnapshot? clash = all.FirstOrDefault(
                    journey => journey.ParentId == target.Id && namesAtParent.Contains(journey.Name));
                if (clash is not null)
                {
                    return OperationResult<bool>.Conflict(
                        ErrorCodes.DuplicateJourneyName,
                        "A journey with that name already exists there.",
                        new Dictionary<string, object> { ["name"] = clash.Name });
                }

                List<long> tripIds = await TripQueries.Rows(_context, deviceRowId)
                    .Where(row => row.EffectiveJourneyId == journeyId)
                    .Select(row => row.Id)
                    .ToListAsync(attemptToken);

                Guid? parentId = target.ParentId;
                bool pinToParent = parentId is not null;
                await _context.Trips
                    .Where(trip => trip.DeviceId == deviceRowId && tripIds.Contains(trip.Id))
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(trip => trip.JourneyId, parentId)
                            .SetProperty(trip => trip.JourneyPinned, pinToParent),
                        attemptToken);

                await _context.Journeys
                    .Where(journey => journey.DeviceId == deviceRowId && journey.ParentId == journeyId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(journey => journey.ParentId, parentId), attemptToken);

                await _context.Journeys
                    .Where(journey => journey.Id == journeyId)
                    .ExecuteDeleteAsync(attemptToken);

                await transaction.CommitAsync(attemptToken);

                _logger.LogInformation(
                    "User {UserId} deleted journey {JourneyId} on device {DeviceId}; {TripCount} trip(s) moved up",
                    userId,
                    journeyId,
                    access.DeviceId,
                    tripIds.Count);

                return OperationResult<bool>.Success(true);
            },
            cancellationToken);

        return result;
    }

    private async Task<OperationResult<JourneyDto>> SaveAsync(
        int userId,
        string deviceId,
        Guid? journeyId,
        JourneySaveRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DeviceAccessContext? access = await _authorizer.ResolveAsync(userId, deviceId, cancellationToken);
        if (access is null)
        {
            return OperationResult<JourneyDto>.NotFound(ErrorCodes.NoSuchDevice, "No such device.");
        }

        if (!access.Permissions.CanModifySettings)
        {
            return Forbidden<JourneyDto>();
        }

        string name = request.Name.Trim();
        if (name.Length == 0)
        {
            return OperationResult<JourneyDto>.Invalid(ErrorCodes.ValidationFailed, "A journey needs a name.");
        }

        if (request.RangeFrom.HasValue != request.RangeTo.HasValue)
        {
            return OperationResult<JourneyDto>.Invalid(ErrorCodes.JourneyRangeInvalid, "A range needs both a start and an end.");
        }

        DateTime? rangeFrom = request.RangeFrom.HasValue ? AsUtc(request.RangeFrom.Value) : null;
        DateTime? rangeTo = request.RangeTo.HasValue ? AsUtc(request.RangeTo.Value) : null;
        if (rangeFrom >= rangeTo)
        {
            return OperationResult<JourneyDto>.Invalid(ErrorCodes.JourneyRangeInvalid, "The range must end after it starts.");
        }

        Guid deviceRowId = access.DeviceRowId;
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        OperationResult<Guid> saved = await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                _context.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(attemptToken);
                await LockDeviceJourneysAsync(deviceRowId, attemptToken);

                List<JourneySnapshot> all = await LoadSnapshotsAsync(deviceRowId, attemptToken);

                Journey? entity = null;
                if (journeyId is Guid existingId)
                {
                    entity = await _context.Journeys.SingleOrDefaultAsync(
                        journey => journey.Id == existingId && journey.DeviceId == deviceRowId,
                        attemptToken);
                    if (entity is null)
                    {
                        return OperationResult<Guid>.NotFound(ErrorCodes.NoSuchJourney, "No such journey.");
                    }
                }
                else if (all.Count >= TripRules.MaxJourneysPerDevice)
                {
                    return OperationResult<Guid>.Conflict(
                        ErrorCodes.TooManyJourneys,
                        "This device already has as many journeys as allowed.",
                        new Dictionary<string, object> { ["max"] = TripRules.MaxJourneysPerDevice });
                }

                OperationResult<Guid>? refused = Validate(journeyId, request.ParentId, name, rangeFrom, rangeTo, all);
                if (refused is not null)
                {
                    return refused;
                }

                DateTime now = DateTime.UtcNow;
                if (entity is null)
                {
                    entity = new Journey { DeviceId = deviceRowId, CreatedAt = now };
                    _context.Journeys.Add(entity);
                }

                entity.Name = name;
                entity.ParentId = request.ParentId;
                entity.RangeFrom = rangeFrom;
                entity.RangeTo = rangeTo;
                entity.UpdatedAt = now;
                await _context.SaveChangesAsync(attemptToken);

                if (request.IncludePinned && rangeFrom is DateTime from && rangeTo is DateTime to)
                {
                    // "Every trip between these dates goes here": hand trips the user had
                    // placed elsewhere back to the ranges, which now put them in this one.
                    Guid savedId = entity.Id;
                    await _context.Trips
                        .Where(trip => trip.DeviceId == deviceRowId
                            && trip.JourneyPinned
                            && trip.StartTime >= from
                            && trip.StartTime < to
                            && (trip.JourneyId == null || trip.JourneyId != savedId))
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(trip => trip.JourneyId, (Guid?)null)
                                .SetProperty(trip => trip.JourneyPinned, false),
                            attemptToken);
                }

                await transaction.CommitAsync(attemptToken);
                return OperationResult<Guid>.Success(entity.Id);
            },
            cancellationToken);

        if (!saved.IsSuccess)
        {
            return OperationResult<JourneyDto>.Failed(
                saved.Outcome,
                new ServiceError(saved.Code ?? ErrorCodes.ValidationFailed, saved.Detail ?? string.Empty, saved.Parameters));
        }

        JourneyListDto list = await BuildListAsync(deviceRowId, cancellationToken);
        return OperationResult<JourneyDto>.Success(list.Journeys.Single(journey => journey.Id == saved.Value));
    }

    // The rules a save must pass, against the device's journeys as they are under the lock.
    private static OperationResult<Guid>? Validate(
        Guid? journeyId,
        Guid? parentId,
        string name,
        DateTime? rangeFrom,
        DateTime? rangeTo,
        List<JourneySnapshot> all)
    {
        switch (JourneyTreeRules.CheckPlacement(journeyId, parentId, all))
        {
            case JourneyPlacementProblem.NoSuchJourney:
                return OperationResult<Guid>.NotFound(ErrorCodes.NoSuchJourney, "No such journey.");
            case JourneyPlacementProblem.Cycle:
                return OperationResult<Guid>.Invalid(ErrorCodes.JourneyCycle, "A journey cannot be moved inside itself.");
            case JourneyPlacementProblem.TooDeep:
                return OperationResult<Guid>.Invalid(
                    ErrorCodes.JourneyTooDeep,
                    "Journeys cannot be nested that deep.",
                    new Dictionary<string, object> { ["max"] = TripRules.MaxJourneyDepth });
        }

        bool nameTaken = all.Any(journey => journey.Id != journeyId
            && journey.ParentId == parentId
            && string.Equals(journey.Name, name, StringComparison.OrdinalIgnoreCase));
        if (nameTaken)
        {
            return OperationResult<Guid>.Conflict(
                ErrorCodes.DuplicateJourneyName,
                "A journey with that name already exists there.",
                new Dictionary<string, object> { ["name"] = name });
        }

        if (rangeFrom is DateTime from && rangeTo is DateTime to
            && JourneyTreeRules.FindRangeConflict(journeyId, from, to, all) is JourneySnapshot other)
        {
            return OperationResult<Guid>.Conflict(
                ErrorCodes.JourneyRangeOverlap,
                "The dates partly overlap another journey's dates.",
                new Dictionary<string, object> { ["name"] = other.Name });
        }

        return null;
    }

    // Serialises journey writes per device for the length of the transaction.
    private Task<int> LockDeviceJourneysAsync(Guid deviceRowId, CancellationToken cancellationToken) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({deviceRowId.ToString()}, 0))",
            cancellationToken);

    private Task<List<JourneySnapshot>> LoadSnapshotsAsync(Guid deviceRowId, CancellationToken cancellationToken) =>
        _context.Journeys
            .AsNoTracking()
            .Where(journey => journey.DeviceId == deviceRowId)
            .Select(journey => new JourneySnapshot(journey.Id, journey.ParentId, journey.Name, journey.RangeFrom, journey.RangeTo))
            .ToListAsync(cancellationToken);

    // The flat list with direct and rolled-up trip counts. One query for the journeys and
    // one for every trip's effective journey; the roll-up is done here, deepest first.
    private async Task<JourneyListDto> BuildListAsync(Guid deviceRowId, CancellationToken cancellationToken)
    {
        List<JourneySnapshot> journeys = await LoadSnapshotsAsync(deviceRowId, cancellationToken);
        List<Guid?> effective = await TripQueries.Rows(_context, deviceRowId)
            .Select(row => row.EffectiveJourneyId)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, int> direct = effective
            .Where(id => id is not null)
            .GroupBy(id => id!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        Dictionary<Guid, JourneySnapshot> byId = journeys.ToDictionary(journey => journey.Id);
        Dictionary<Guid, int> total = journeys.ToDictionary(journey => journey.Id, journey => direct.GetValueOrDefault(journey.Id));
        foreach (JourneySnapshot journey in journeys.OrderByDescending(node => JourneyTreeRules.LevelUnder(node.ParentId, byId)))
        {
            if (journey.ParentId is Guid parent && total.ContainsKey(parent))
            {
                total[parent] += total[journey.Id];
            }
        }

        List<JourneyDto> dtos = journeys
            .OrderBy(journey => journey.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(journey => new JourneyDto(
                journey.Id,
                journey.ParentId,
                journey.Name,
                journey.RangeFrom,
                journey.RangeTo,
                direct.GetValueOrDefault(journey.Id),
                total[journey.Id]))
            .ToList();

        return new JourneyListDto(dtos, effective.Count, effective.Count(id => id is null));
    }

    private static OperationResult<T> Forbidden<T>() =>
        OperationResult<T>.Forbidden(
            ErrorCodes.NoPermissionManageTrips,
            "You do not have permission to organise this device's trips.");

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
