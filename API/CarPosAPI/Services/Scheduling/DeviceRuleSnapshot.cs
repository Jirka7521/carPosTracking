namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// A rule paired with the device it belongs to, so a fleet-wide rule query can be
/// grouped after the fact instead of run once per device.
///
/// <para>
/// A named record because this project does not use <c>var</c> and so cannot project
/// into an anonymous type. It lives only between
/// <see cref="ScheduleReconciler"/>'s query and the lookup it is folded into.
/// </para>
/// </summary>
/// <param name="DeviceRowId">Internal id of the device the rule belongs to.</param>
/// <param name="Rule">The rule, in the shape the evaluator takes.</param>
internal sealed record DeviceRuleSnapshot(Guid DeviceRowId, ScheduleRuleSnapshot Rule);
