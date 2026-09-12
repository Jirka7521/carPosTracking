using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Implements <see cref="IShareViewService"/>.
///
/// <para>
/// Three rules, all enforced here rather than anywhere a client could influence
/// them: the link is re-read and re-checked on <em>every</em> call; the time range
/// is clamped to the share's window whatever was asked for; and the projection
/// carries only the fields the creator opted into.
/// </para>
///
/// Scoped — it owns a scoped <see cref="CarPosDbContext"/>.
/// </summary>
internal sealed class ShareViewService : IShareViewService
{
    /// <summary>
    /// Row cap for a full-track share, matching
    /// <c>PositionQueryService.MaxPositionsPerQuery</c> for the same reason: a track
    /// longer than this is one no browser can usefully draw, and an uncapped query
    /// over a table this size is how the API falls over.
    /// </summary>
    private const int MaxPositionsPerQuery = 1000;

    private readonly CarPosDbContext _context;

    /// <summary>Creates the service.</summary>
    /// <param name="context">Scoped database context.</param>
    public ShareViewService(CarPosDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SharedViewDto>> GetAsync(
        Guid shareId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        // The token said which share; it did not say the share is still open. This
        // read is what makes "revoke" mean "now" rather than "when their token runs
        // out", so it happens before anything else and on every single request.
        ShareLinkViewRow? link = await _context.ShareLinks
            .AsNoTracking()
            .Where(candidate => candidate.Id == shareId)
            .Select(candidate => new ShareLinkViewRow(
                candidate.DeviceId,
                candidate.Label,
                candidate.ValidFrom,
                candidate.ValidUntil,
                candidate.Scope,
                candidate.IncludeSpeed,
                candidate.IncludeTelemetry,
                candidate.RevokedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (link is null || link.RevokedAt.HasValue)
        {
            return OperationResult<SharedViewDto>.NotFound("This link is no longer available.");
        }

        DateTime nowUtc = DateTime.UtcNow;

        if (nowUtc > link.ValidUntil || nowUtc < link.ValidFrom)
        {
            return OperationResult<SharedViewDto>.NotFound("This link is no longer available.");
        }

        ShareSessionDto session = new ShareSessionDto(
            link.Label,
            link.ValidFrom,
            link.ValidUntil,
            link.Scope == ShareScope.FullTrack ? ShareScopeNames.FullTrack : ShareScopeNames.LatestOnly,
            link.IncludeSpeed,
            link.IncludeTelemetry);

        List<SharedPositionDto> positions = await QueryAsync(link, fromUtc, toUtc, cancellationToken);

        return OperationResult<SharedViewDto>.Success(new SharedViewDto(session, positions));
    }

    /// <summary>
    /// Runs the bounded, filtered position query for a share.
    /// </summary>
    /// <param name="link">The share, already proved live.</param>
    /// <param name="fromUtc">Requested lower bound, if any.</param>
    /// <param name="toUtc">Requested upper bound, if any.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The fixes the share exposes, newest first.</returns>
    private async Task<List<SharedPositionDto>> QueryAsync(
        ShareLinkViewRow link,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        bool latestOnly = link.Scope == ShareScope.LatestOnly;

        // ------------------------------------------------------------------
        // A latest-only share ignores the requested range entirely, and that is
        // not a simplification — it closes a hole.
        //
        // Honouring an upper bound while returning one row would let a visitor
        // reconstruct the whole track: ask for the newest fix before 14:00, then
        // before that one, and so on, walking the window backwards a point at a
        // time until they have every position the "current position only" share
        // was chosen precisely to withhold. The range controls are hidden in the
        // UI for this scope, but the UI is not what enforces it.
        // ------------------------------------------------------------------
        DateTime from = latestOnly
            ? link.ValidFrom
            : Later(NormaliseToUtc(fromUtc) ?? link.ValidFrom, link.ValidFrom);

        DateTime to = latestOnly
            ? link.ValidUntil
            : Earlier(NormaliseToUtc(toUtc) ?? link.ValidUntil, link.ValidUntil);

        if (to < from)
        {
            // A range entirely outside the window clamps to an empty one. Answering
            // with no fixes is the correct result, not an error.
            return [];
        }

        bool includeSpeed = link.IncludeSpeed;
        bool includeTelemetry = link.IncludeTelemetry;

        // Filtering, ordering, the cap and the opt-in fields all happen in SQL, so a
        // value the share does not cover is never serialised and a track longer than
        // the cap is never materialised.
        return await _context.Positions
            .AsNoTracking()
            .Where(position => position.DeviceId == link.DeviceId)
            .Where(position => position.FixTime >= from && position.FixTime <= to)
            .OrderByDescending(position => position.FixTime)
            .Take(latestOnly ? 1 : MaxPositionsPerQuery)
            .Select(position => new SharedPositionDto(
                position.FixTime,
                position.Latitude,
                position.Longitude,
                includeSpeed ? position.SpeedKmph : null,
                includeTelemetry ? position.BatteryPct : null,
                includeTelemetry ? position.TemperatureC : null))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Returns the later of two instants.</summary>
    /// <param name="first">One instant.</param>
    /// <param name="second">The other.</param>
    /// <returns>Whichever is later.</returns>
    private static DateTime Later(DateTime first, DateTime second)
    {
        return first > second ? first : second;
    }

    /// <summary>Returns the earlier of two instants.</summary>
    /// <param name="first">One instant.</param>
    /// <param name="second">The other.</param>
    /// <returns>Whichever is earlier.</returns>
    private static DateTime Earlier(DateTime first, DateTime second)
    {
        return first < second ? first : second;
    }

    /// <summary>
    /// Forces a query-string bound to <see cref="DateTimeKind.Utc"/>.
    ///
    /// Same reasoning as <c>PositionQueryService.NormaliseToUtc</c>: <c>fix_time</c>
    /// is <c>timestamptz</c> and Npgsql refuses a parameter of any other kind. Local
    /// values are converted rather than re-labelled, so an ISO string with an offset
    /// compares as the instant it names.
    /// </summary>
    /// <param name="value">A bound as model-bound from the query string, or null.</param>
    /// <returns>The same instant with <see cref="DateTimeKind.Utc"/>, or null.</returns>
    private static DateTime? NormaliseToUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };
    }
}
