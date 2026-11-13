using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// A device's location rules: circles on the map whose trips (starting or ending there)
/// join a chosen journey. Rules are created, edited while in force, and ended — never
/// deleted, so they remain as history.
/// </summary>
public interface ILocationRuleService
{
    /// <summary>Every rule of the device, in force first, then the ended ones, each with its trip count.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The rules, or why not.</returns>
    Task<OperationResult<IReadOnlyList<LocationRuleDto>>> ListAsync(int userId, string deviceId, CancellationToken cancellationToken);

    /// <summary>What a rule drawn so far would catch, computed in the database.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="query">The circle, the kinds of match and the valid-from date.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Counts and points, or why not.</returns>
    Task<OperationResult<LocationRulePreviewDto>> PreviewAsync(
        int userId,
        string deviceId,
        LocationRulePreviewQuery query,
        CancellationToken cancellationToken);

    /// <summary>Creates a rule.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The rule.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new rule, or why not.</returns>
    Task<OperationResult<LocationRuleDto>> CreateAsync(
        int userId,
        string deviceId,
        LocationRuleSaveRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Replaces a rule that is still in force.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="request">The rule's new values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The saved rule, or why not.</returns>
    Task<OperationResult<LocationRuleDto>> UpdateAsync(
        int userId,
        string deviceId,
        Guid ruleId,
        LocationRuleSaveRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Ends a rule now; the trips it placed so far stay, and it remains as history.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The ended rule, or why not.</returns>
    Task<OperationResult<LocationRuleDto>> EndAsync(int userId, string deviceId, Guid ruleId, CancellationToken cancellationToken);
}
