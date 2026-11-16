using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// The writes to <c>journey_location_rules</c>, shared by the rule endpoints
/// (<see cref="LocationRuleService"/>) and the one place a journey's dialog saves together
/// with the journey (<see cref="JourneyService"/>), so both refuse the same things in the
/// same words.
///
/// <para>
/// The caller has begun a transaction and holds <see cref="TripOrganisationLock"/> — the
/// per-device caps are only true under it — and it saves and commits. A refusal is
/// returned, never thrown; the caller then leaves without committing, so nothing of the
/// request is kept.
/// </para>
/// </summary>
internal static class LocationRuleWriter
{
    /// <summary>What a rule says, as the user set it.</summary>
    /// <param name="Name">Optional label; blank means none.</param>
    /// <param name="Latitude">Centre latitude, decimal degrees.</param>
    /// <param name="Longitude">Centre longitude, decimal degrees.</param>
    /// <param name="RadiusMeters">Radius of the circle in metres.</param>
    /// <param name="MatchStart">Match trips starting in the circle.</param>
    /// <param name="MatchEnd">Match trips ending in the circle.</param>
    /// <param name="ValidFrom">First instant the rule covers (any kind; stored as UTC).</param>
    internal sealed record RuleValues(
        string? Name,
        double Latitude,
        double Longitude,
        int RadiusMeters,
        bool MatchStart,
        bool MatchEnd,
        DateTime ValidFrom);

    /// <summary>
    /// The rules a rule's values must pass. The request attributes cover most of them at
    /// the top level of a body; checked again here because a nested body (the journey's
    /// place) is the client's word too, and because "at least one end" is not an
    /// attribute at all.
    /// </summary>
    /// <param name="values">The values to check.</param>
    /// <returns>The refusal, or <c>null</c> when they pass.</returns>
    public static OperationResult<JourneyLocationRule>? Check(RuleValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!values.MatchStart && !values.MatchEnd)
        {
            return OperationResult<JourneyLocationRule>.Invalid(
                ErrorCodes.LocationRuleInvalid,
                "A location rule must match where trips start, where they end, or both.");
        }

        bool placeValid = double.IsFinite(values.Latitude)
            && double.IsFinite(values.Longitude)
            && Math.Abs(values.Latitude) <= 90.0
            && Math.Abs(values.Longitude) <= 180.0;
        bool radiusValid = values.RadiusMeters >= TripRules.MinLocationRuleRadiusMeters
            && values.RadiusMeters <= TripRules.MaxLocationRuleRadiusMeters;
        if (!placeValid || !radiusValid)
        {
            return OperationResult<JourneyLocationRule>.Invalid(
                ErrorCodes.LocationRuleInvalid,
                "The place must be a valid latitude and longitude, and the radius within the allowed range.");
        }

        if (values.Name is not null && values.Name.Trim().Length > TripRules.MaxNameLength)
        {
            return OperationResult<JourneyLocationRule>.Invalid(
                ErrorCodes.ValidationFailed,
                "The name is too long.",
                new Dictionary<string, object> { ["max"] = TripRules.MaxNameLength });
        }

        return null;
    }

    /// <summary>
    /// Creates a rule (<paramref name="ruleId"/> null) or replaces one still in force. The
    /// caller checks that <paramref name="journeyId"/> is one of the device's journeys.
    /// </summary>
    /// <param name="context">The context, inside the caller's transaction.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="ruleId">The rule to replace, or <c>null</c> for a new one.</param>
    /// <param name="journeyId">The journey matching trips join.</param>
    /// <param name="onlyInJourney">
    /// When set, the rule being replaced must belong to this journey now — a journey's
    /// dialog edits its own place, never another journey's.
    /// </param>
    /// <param name="values">What the rule says.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The tracked rule, not yet saved; or the refusal.</returns>
    public static async Task<OperationResult<JourneyLocationRule>> SetAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        Guid? ruleId,
        Guid journeyId,
        Guid? onlyInJourney,
        RuleValues values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        OperationResult<JourneyLocationRule>? invalid = Check(values);
        if (invalid is not null)
        {
            return invalid;
        }

        JourneyLocationRule rule;
        if (ruleId is Guid existingId)
        {
            OperationResult<JourneyLocationRule> found =
                await FindInForceAsync(context, deviceRowId, existingId, onlyInJourney, cancellationToken);
            if (!found.IsSuccess)
            {
                return found;
            }

            rule = found.Value!;
        }
        else
        {
            OperationResult<JourneyLocationRule>? full = await CheckCapsAsync(context, deviceRowId, cancellationToken);
            if (full is not null)
            {
                return full;
            }

            rule = new JourneyLocationRule { DeviceId = deviceRowId, CreatedAt = DateTime.UtcNow };
            context.JourneyLocationRules.Add(rule);
        }

        rule.JourneyId = journeyId;
        rule.Name = string.IsNullOrWhiteSpace(values.Name) ? null : values.Name.Trim();
        rule.Latitude = values.Latitude;
        rule.Longitude = values.Longitude;
        rule.RadiusMeters = values.RadiusMeters;
        rule.MatchStart = values.MatchStart;
        rule.MatchEnd = values.MatchEnd;
        rule.ValidFrom = AsUtc(values.ValidFrom);
        rule.UpdatedAt = DateTime.UtcNow;

        return OperationResult<JourneyLocationRule>.Success(rule);
    }

    /// <summary>
    /// Ends a rule in force: it stops placing trips that start from now on and keeps the
    /// ones it placed, as history.
    /// </summary>
    /// <param name="context">The context, inside the caller's transaction.</param>
    /// <param name="deviceRowId">The device's internal id.</param>
    /// <param name="ruleId">The rule to end.</param>
    /// <param name="onlyInJourney">When set, the rule must belong to this journey now.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The tracked rule, not yet saved; or the refusal.</returns>
    public static async Task<OperationResult<JourneyLocationRule>> EndAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        Guid ruleId,
        Guid? onlyInJourney,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        OperationResult<JourneyLocationRule> found =
            await FindInForceAsync(context, deviceRowId, ruleId, onlyInJourney, cancellationToken);
        if (!found.IsSuccess)
        {
            return found;
        }

        JourneyLocationRule rule = found.Value!;
        DateTime now = DateTime.UtcNow;
        rule.EndedAt = now;
        rule.EndReason = LocationRuleEndReasons.User;
        rule.UpdatedAt = now;
        return found;
    }

    /// <summary>Carries a refusal over to the caller's result type.</summary>
    /// <typeparam name="T">The caller's value type.</typeparam>
    /// <param name="failure">A failed result from this class.</param>
    /// <returns>The same failure, typed for the caller.</returns>
    public static OperationResult<T> Relay<T>(OperationResult<JourneyLocationRule> failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return OperationResult<T>.Failed(
            failure.Outcome,
            new ServiceError(failure.Code ?? ErrorCodes.ValidationFailed, failure.Detail ?? string.Empty, failure.Parameters));
    }

    // The rule, if it is the device's, still in force, and (when asked) in that journey.
    // Another journey's rule reads as "no such rule": the journey dialog has no business
    // with it.
    private static async Task<OperationResult<JourneyLocationRule>> FindInForceAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        Guid ruleId,
        Guid? onlyInJourney,
        CancellationToken cancellationToken)
    {
        JourneyLocationRule? rule = await context.JourneyLocationRules.SingleOrDefaultAsync(
            candidate => candidate.Id == ruleId && candidate.DeviceId == deviceRowId,
            cancellationToken);
        if (rule is null || (onlyInJourney is Guid journeyId && rule.JourneyId != journeyId))
        {
            return OperationResult<JourneyLocationRule>.NotFound(ErrorCodes.NoSuchLocationRule, "No such location rule.");
        }

        if (rule.EndedAt is not null)
        {
            return OperationResult<JourneyLocationRule>.Conflict(
                ErrorCodes.LocationRuleEnded,
                "This location rule has ended and is kept as history; create a new one instead.");
        }

        return OperationResult<JourneyLocationRule>.Success(rule);
    }

    // The per-device caps: rules in force (they cost every trip read) and rules in all
    // (the history must not grow without bound).
    private static async Task<OperationResult<JourneyLocationRule>?> CheckCapsAsync(
        CarPosDbContext context,
        Guid deviceRowId,
        CancellationToken cancellationToken)
    {
        int total = await context.JourneyLocationRules.CountAsync(rule => rule.DeviceId == deviceRowId, cancellationToken);
        if (total >= TripRules.MaxLocationRulesPerDevice)
        {
            return TooMany(TripRules.MaxLocationRulesPerDevice);
        }

        int active = await context.JourneyLocationRules.CountAsync(
            rule => rule.DeviceId == deviceRowId && rule.EndedAt == null,
            cancellationToken);
        return active >= TripRules.MaxActiveLocationRulesPerDevice
            ? TooMany(TripRules.MaxActiveLocationRulesPerDevice)
            : null;
    }

    private static OperationResult<JourneyLocationRule> TooMany(int max) =>
        OperationResult<JourneyLocationRule>.Conflict(
            ErrorCodes.TooManyLocationRules,
            "This device already has as many location rules as allowed.",
            new Dictionary<string, object> { ["max"] = max });

    // Instants arrive with whatever kind model binding gave them; the column is
    // timestamptz and Npgsql insists on UTC.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
