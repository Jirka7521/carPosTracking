using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Reads and edits a device's schedule — its profiles, its weekly rules, and whether
/// they are in charge — and applies the result immediately whenever an edit changes
/// what the device should be running.
///
/// <para>
/// Separate from <see cref="IDeviceConfigService"/> for the same reason that one is
/// separate from <see cref="IDeviceService"/>: a different resource with a different
/// lifecycle. Settings accumulate an immutable history; a schedule is a small, mutable
/// set of rows that <em>produces</em> that history.
/// </para>
///
/// <para>
/// <b>Every method returns the whole state.</b> Adding a rule moves the next switch,
/// deleting a profile can change what is in force this second, and enabling a schedule
/// changes both — so a client that had to re-fetch would always have a window in which
/// it rendered something that was true a moment ago. Returning the recomputed state
/// closes that window by construction.
/// </para>
///
/// <para>
/// Every method is gated on <c>CanModifySettings</c>, including the reads: a schedule is
/// the same operational tuning the settings panel exposes, expressed differently.
/// </para>
/// </summary>
public interface IDeviceConfigScheduleService
{
    /// <summary>Returns the device's profiles, rules, live status and any override.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The state, 403 without <c>CanModifySettings</c>, or 404 when not visible.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> GetStateAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Turns the schedule on or off and sets its fallback profile. Enabling applies the
    /// profile in force straight away rather than waiting for the next reconciling pass.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The new schedule settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 400 when enabling without a usable fallback, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> UpdateSettingsAsync(
        int userId,
        string deviceId,
        UpdateDeviceScheduleRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Creates a profile.</summary>
    /// <param name="userId">The caller, recorded as its author.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">Name and the seven values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 409 on a duplicate name or a full device, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> CreateProfileAsync(
        int userId,
        string deviceId,
        SaveConfigProfileRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a profile's name and values. If the profile is currently in force the
    /// new values are applied to the device immediately.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="profileId">The profile to replace.</param>
    /// <param name="request">Its new name and values.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 409 on a duplicate name, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> UpdateProfileAsync(
        int userId,
        string deviceId,
        Guid profileId,
        SaveConfigProfileRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Deletes a profile no rule and no fallback still points at.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="profileId">The profile to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 409 while it is still referenced, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> DeleteProfileAsync(
        int userId,
        string deviceId,
        Guid profileId,
        CancellationToken cancellationToken);

    /// <summary>Creates a weekly rule.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The window, in UTC minutes.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 400 for a profile of another device, 409 when full, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> CreateRuleAsync(
        int userId,
        string deviceId,
        SaveScheduleRuleRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Replaces a rule's window, profile, priority and enabled flag.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule to replace.</param>
    /// <param name="request">Its new window.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 400 for a profile of another device, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> UpdateRuleAsync(
        int userId,
        string deviceId,
        Guid ruleId,
        SaveScheduleRuleRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Deletes a rule.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ruleId">The rule to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> DeleteRuleAsync(
        int userId,
        string deviceId,
        Guid ruleId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends a manual override early and reapplies the profile the schedule says should
    /// be in force. Harmless when there is no override — it simply reasserts what is
    /// already true.
    /// </summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new state, 400 when no schedule is enabled, 403, or 404.</returns>
    Task<OperationResult<DeviceScheduleStateDto>> ResumeAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);
}
