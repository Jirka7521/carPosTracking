using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// A profile reduced to what applying it needs: its id, its name for the log line, and
/// its values.
///
/// <para>
/// The name is carried because a log entry saying a device switched to
/// <c>7f3a…</c> is one nobody can act on, and looking it up afterwards means finding a
/// profile that may by then have been renamed.
/// </para>
/// </summary>
/// <param name="ProfileId">The profile, recorded on the revision it produces.</param>
/// <param name="Name">Its name, for logging.</param>
/// <param name="Values">The seven settings to publish.</param>
internal sealed record ProfileValues(Guid ProfileId, string Name, DeviceConfigValuesDto Values);
