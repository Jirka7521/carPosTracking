namespace CarPosAPI.Dtos;

/// <summary>
/// What the schedule resolves to right now, and what it changes to next.
///
/// <para>
/// Computed server-side and never by the client, even though the client has the rules
/// and could. The server is the thing that <em>acts</em> on this answer, and a
/// dashboard that computed its own would eventually disagree with the tracker over a
/// rounding rule or a wrap — and be believed, because it is the one on screen.
/// </para>
///
/// <para>
/// Null throughout when the schedule is off: there is no active profile to name,
/// because nothing is acting on the rules.
/// </para>
/// </summary>
/// <param name="ActiveProfileId">The profile currently in force.</param>
/// <param name="ActiveProfileName">Its name, for the banner.</param>
/// <param name="ActiveRuleId">The rule whose window matched, or null when the fallback did.</param>
/// <param name="ActiveSince">
/// When the current stretch began (UTC), or null when the schedule resolves the same
/// way all week and there is no meaningful "since".
/// </param>
/// <param name="NextChangeAt">When the active profile next changes (UTC), or null when it never does.</param>
/// <param name="NextProfileId">The profile taking over then.</param>
/// <param name="NextProfileName">Its name.</param>
/// <param name="ReportedProfileId">
/// The profile the <em>device</em> last said it was running. Null on firmware that
/// predates device-side scheduling, on a device that has not reported since gaining a
/// schedule, and on one whose slot no longer maps to a profile.
/// </param>
/// <param name="ReportedProfileName">Its name, for the banner.</param>
/// <param name="ReportedAt">
/// The <b>fix time</b> of the report that said so — not when it arrived. A device
/// draining a backlog sends fixes from all over the week, and this is the instant the
/// reported profile was actually true of.
/// </param>
/// <param name="BundleVersion">The schedule bundle revision the server has published.</param>
/// <param name="ReportedScheduleVersion">
/// The bundle revision the device reported holding. Lower than
/// <paramref name="BundleVersion"/> means a change is in flight, not that anything is
/// wrong.
/// </param>
/// <param name="IsDeviceInStep">
/// Whether the device was running the profile the rules called for <em>at
/// <paramref name="ReportedAt"/></em>. Null when there is nothing to compare — no
/// report, or a device that does not switch itself and is driven by the server instead.
///
/// <para>
/// Deliberately not computed against "now": a fix taken five minutes before a boundary
/// should report the profile that was in force five minutes before the boundary, and
/// judging it against the present would light this up as a fault after every single
/// switch.
/// </para>
/// </param>
public sealed record DeviceScheduleStatusDto(
    Guid? ActiveProfileId,
    string? ActiveProfileName,
    Guid? ActiveRuleId,
    DateTime? ActiveSince,
    DateTime? NextChangeAt,
    Guid? NextProfileId,
    string? NextProfileName,
    Guid? ReportedProfileId,
    string? ReportedProfileName,
    DateTime? ReportedAt,
    int BundleVersion,
    int? ReportedScheduleVersion,
    bool? IsDeviceInStep);
