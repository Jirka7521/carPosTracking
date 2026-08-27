namespace CarPosAPI.Data.Entities;

/// <summary>
/// What caused a <see cref="DeviceConfigVersion"/> row to exist.
///
/// <para>
/// Without this the history is misleading rather than merely incomplete: a revision
/// the scheduler wrote has no <see cref="DeviceConfigVersion.CreatedByUserId"/>, and
/// so renders identically to the two authorless rows that predate remote settings —
/// "created with the device" and "seeded by the migration". Somebody reading the
/// history to work out why a tracker changed cadence at 22:00 would find a blank.
/// </para>
///
/// <para>
/// Stored as its <c>int</c> value, which is why the members are numbered explicitly:
/// the numbers are in the database and may not be renumbered, though new members may
/// be appended.
/// </para>
/// </summary>
public enum ConfigRevisionSource
{
    /// <summary>
    /// A person saved it from the dashboard — or nobody did, for the rows that
    /// predate schedules entirely. The default, so every existing row keeps its
    /// current meaning without the migration having to guess.
    /// </summary>
    Manual = 0,

    /// <summary>
    /// The scheduler applied a profile because a time window began or ended. The
    /// profile it came from is in <see cref="DeviceConfigVersion.SourceProfileId"/>.
    /// </summary>
    Schedule = 1,
}
