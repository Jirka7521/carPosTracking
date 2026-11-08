namespace CarPosAPI.Data.Entities;

/// <summary>
/// Per-device bookkeeping for the trip detection worker — not user data.
///
/// <para>
/// The row doubles as the device's <b>trip lock</b>: every writer of a device's trips
/// (the worker's apply step, both erasure paths, a settings save, journey writes)
/// locks it <c>FOR UPDATE</c> first, so writers serialise and always take locks in the
/// same order. <see cref="Epoch"/> lets the worker notice that an erasure or settings
/// change happened while it was computing, and throw its now-stale result away.
/// </para>
/// Mapped by <see cref="Configurations.TripDetectionStateConfiguration"/>.
/// </summary>
public sealed class TripDetectionState
{
    /// <summary>The device this state belongs to (PK and FK).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// Earliest fix time (UTC) whose trips need recomputing, or null when the device's
    /// trips are up to date. Only ever lowered by markers, cleared by the worker.
    /// </summary>
    public DateTime? DirtyFrom { get; set; }

    /// <summary>
    /// Bumped by every change that invalidates an in-flight computation (position
    /// erasure, settings save). The worker discards a result computed under an older epoch.
    /// </summary>
    public int Epoch { get; set; }

    /// <summary>When the worker last finished a pass for this device (UTC), or null.</summary>
    public DateTime? LastPassAt { get; set; }
}
