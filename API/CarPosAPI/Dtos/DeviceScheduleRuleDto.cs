namespace CarPosAPI.Dtos;

/// <summary>
/// One weekly window as the dashboard sees it. Every time here is <b>UTC</b>; the
/// browser converts.
///
/// <para>
/// <paramref name="ProfileName"/> is carried alongside the id even though the client
/// already has the profile list, because the alternative is a lookup in every row of
/// every rendering — a rule list, a timeline block, a status banner — and one of them
/// would eventually be written without it and show a bare Guid.
/// </para>
/// </summary>
/// <param name="Id">Stable identifier for editing and deleting.</param>
/// <param name="ProfileId">The profile this window puts in force.</param>
/// <param name="ProfileName">That profile's name, resolved server-side.</param>
/// <param name="DaysMaskUtc">7-bit mask of UTC weekdays the window opens on; bit 0 is Sunday.</param>
/// <param name="StartMinuteUtc">Minutes past UTC midnight at which it opens; 0–1439.</param>
/// <param name="DurationMinutes">How long it stays open; 1–1440, end exclusive.</param>
/// <param name="Priority">Lower wins where windows overlap.</param>
/// <param name="IsEnabled">False parks the rule without discarding its times.</param>
public sealed record DeviceScheduleRuleDto(
    Guid Id,
    Guid ProfileId,
    string ProfileName,
    int DaysMaskUtc,
    int StartMinuteUtc,
    int DurationMinutes,
    int Priority,
    bool IsEnabled);
