namespace CarPosAPI.Dtos;

/// <summary>
/// How often a device is expected to report, so the map can tell a current position
/// from one the tracker should have replaced by now.
///
/// <para>
/// The server works this out rather than the dashboard because it needs the settings
/// and the schedule, which only <c>CanModifySettings</c> may read — while the answer
/// is needed by every viewer of the map. Only the two numbers leave the server.
/// </para>
/// </summary>
/// <param name="MaxIntervalSeconds">
/// The longest the device may stay quiet between reports under any settings that may
/// be in force now (the standby interval of the current plan, as a rule). Null when
/// the device is retired or has no stored settings.
/// </param>
/// <param name="OverdueAfterSeconds">
/// How old the latest fix may get before it counts as overdue: a fixed multiple of
/// <paramref name="MaxIntervalSeconds"/>. Null whenever that is.
/// </param>
public sealed record DeviceReportIntervalDto(
    int? MaxIntervalSeconds,
    int? OverdueAfterSeconds);
