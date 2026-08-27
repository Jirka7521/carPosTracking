using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Creates or replaces one weekly window. <b>All times are UTC minutes</b> — the API
/// has no notion of a local time and never converts one.
///
/// <para>
/// That is a deliberate division of labour rather than an omission. The browser knows
/// the reader's offset, including which side of a DST change today falls on; the server
/// would have to be told, and would then hold a second opinion about it. So the
/// dashboard converts what the reader types into these fields and back again for
/// display, and the server evaluates the one unambiguous representation.
/// </para>
///
/// <para>
/// The consequence, accepted knowingly: a window entered as 22:00 in winter is stored
/// as 21:00 UTC and renders as 23:00 local after the spring change. The stored instant
/// did not move — the local clock did. The dashboard shows both times on every rule so
/// this is visible, and re-entering the time is the fix.
/// </para>
/// </summary>
/// <param name="ProfileId">The profile this window puts in force. Must belong to the same device.</param>
/// <param name="DaysMaskUtc">
/// 7-bit mask of the UTC weekdays the window opens on; bit 0 is Sunday. At least one
/// bit must be set — a rule that can never open is a mistake, and <paramref name="IsEnabled"/>
/// is the honest way to park one.
/// </param>
/// <param name="StartMinuteUtc">Minutes past UTC midnight at which it opens; 0–1439.</param>
/// <param name="DurationMinutes">
/// How long it stays open; 1–1440, end exclusive. A duration rather than an end time
/// because it needs no midnight-wrap convention and survives timezone conversion
/// untouched.
/// </param>
/// <param name="Priority">Lower wins where windows overlap.</param>
/// <param name="IsEnabled">False parks the rule without discarding times somebody worked out.</param>
public sealed record SaveScheduleRuleRequestDto(
    [Required]
    Guid ProfileId,

    [Required]
    [Range(ScheduleRules.MinDaysMask, ScheduleRules.MaxDaysMask)]
    int DaysMaskUtc,

    [Required]
    [Range(ScheduleRules.MinStartMinute, ScheduleRules.MaxStartMinute)]
    int StartMinuteUtc,

    [Required]
    [Range(ScheduleRules.MinDurationMinutes, ScheduleRules.MaxDurationMinutes)]
    int DurationMinutes,

    [Required]
    [Range(ScheduleRules.MinPriority, ScheduleRules.MaxPriority)]
    int Priority,

    [Required]
    bool IsEnabled);
