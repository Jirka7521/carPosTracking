namespace CarPosAPI.Dtos;

/// <summary>
/// A manual change holding the schedule off until the next switch.
///
/// <para>
/// Present only while the override is live; the field is null the rest of the time,
/// which is what the dashboard branches on rather than comparing
/// <paramref name="Until"/> to its own clock. That matters more than it looks: the
/// browser's clock is not the server's, and near the boundary the two would disagree
/// about whether the amber banner should still be up.
/// </para>
/// </summary>
/// <param name="Until">When the schedule takes over again (UTC).</param>
/// <param name="ResumingProfileId">The profile that will be applied then.</param>
/// <param name="ResumingProfileName">Its name — the "the Night profile returns at 06:00" in the banner.</param>
public sealed record DeviceScheduleOverrideDto(
    DateTime Until,
    Guid? ResumingProfileId,
    string? ResumingProfileName);
