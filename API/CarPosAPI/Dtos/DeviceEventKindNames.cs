namespace CarPosAPI.Dtos;

/// <summary>
/// The wire spellings of what kind of thing a device event records — stored as-is in
/// <c>device_events.kind</c> and sent unchanged to the dashboard.
///
/// <para>
/// Strings rather than an enum for the same reason as <see cref="ShareLinkStatusNames"/>
/// and <c>DeviceConfigVersionDto.Source</c>: the wire contract must not depend on the
/// application's JSON enum settings, and a kind added later should read as itself in a
/// client that has not been updated. The database pins the vocabulary with a CHECK
/// constraint (see <c>DeviceEventConfiguration</c>), so a typo cannot be stored.
/// </para>
/// </summary>
public static class DeviceEventKindNames
{
    /// <summary>
    /// The device went off the air: it said why before a planned sleep or shutdown, or
    /// the broker published its Last Will after an unplanned drop.
    /// </summary>
    public const string Offline = "offline";

    /// <summary>
    /// The device came back after a reset worth knowing about — a crash, a watchdog, a
    /// brown-out or a cold power-on — reported by the device itself once it reconnected.
    /// </summary>
    public const string Restart = "restart";

    /// <summary>
    /// The device woke from deep sleep: on its timer, because the car moved, or because
    /// the power switch was turned back on.
    /// </summary>
    public const string Wake = "wake";

    /// <summary>
    /// A step of the device's motion-wake state machine: checking for movement, moving,
    /// parked again, or motion wake switched on or off.
    /// </summary>
    public const string Motion = "motion";
}
