namespace CarPosAPI.Dtos;

/// <summary>
/// Turns a schedule on or off and names its fallback profile.
///
/// <para>
/// The two settle together on purpose. Enabling without a fallback would leave every
/// hour no rule covers undefined — the device would simply keep whatever it last
/// happened to be given, which is the one behaviour a schedule exists to eliminate — so
/// the service rejects that combination rather than letting a client assemble it in two
/// requests with a gap in between.
/// </para>
/// </summary>
/// <param name="Enabled">Whether the rules drive this device's settings.</param>
/// <param name="FallbackProfileId">
/// The profile for uncovered time. Must name a profile of this device, and must be set
/// whenever <paramref name="Enabled"/> is true.
/// </param>
public sealed record UpdateDeviceScheduleRequestDto(
    bool Enabled,
    Guid? FallbackProfileId);
