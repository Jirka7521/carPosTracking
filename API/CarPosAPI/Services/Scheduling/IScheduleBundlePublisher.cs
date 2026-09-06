using CarPosAPI.Data.Entities;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Bumps a device's schedule bundle revision and pushes the rebuilt bundle to the
/// broker, retained.
///
/// <para>
/// Every change that alters what the device should decide goes through here: a profile
/// created, edited or deleted, a rule added, changed or removed, the schedule enabled
/// or disabled, the fallback repointed, an override stamped or resumed. The single
/// entry point is what keeps the revision number honest — and the revision number is
/// what lets <see cref="ScheduleReconciler"/> tell "the device has not received this
/// yet" apart from "the device disagrees with this", which are the same symptom and
/// need opposite responses.
/// </para>
///
/// <para>
/// Scoped, because it writes through the caller's context: the bump belongs to the
/// same unit of work as the change that caused it.
/// </para>
/// </summary>
internal interface IScheduleBundlePublisher
{
    /// <summary>
    /// Bumps <see cref="Device.ScheduleBundleVersion"/>, saves, and publishes the
    /// rebuilt bundle retained.
    ///
    /// Never throws. The rows are what matter and they are committed here; a broker
    /// that is down is caught by the reconnect sweep, exactly as it is for the config
    /// document.
    /// </summary>
    /// <param name="device">The tracked device whose schedule changed.</param>
    /// <param name="cancellationToken">Cancels the save and the publish.</param>
    /// <returns>The bundle revision that was published.</returns>
    Task<int> PublishAsync(Device device, CancellationToken cancellationToken);
}
