using System.Diagnostics.CodeAnalysis;
using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// The one table that turns the firmware's words into stored events and decides how
/// much each one matters.
///
/// <para>
/// Severity lives here and nowhere else on purpose. The device only says what
/// happened — "going to sleep", "battery low", "I restarted after a PANIC", "the
/// accelerometer woke me", "the car is moving" — and never
/// how worrying that is, so a judgement can be changed by redeploying the API instead
/// of reflashing every tracker in the field, and the two can never disagree.
/// </para>
///
/// <para>
/// The device side is snake_case (<c>power_off</c>) and the reset causes are
/// ESP-IDF's own names as <c>ESP32/src/power/BootJournal.cpp</c> prints them
/// (<c>BROWNOUT</c>); the stored side is the API's camelCase vocabulary. This is the
/// boundary between them. <c>DeviceEventClassifierTests</c> pins the table down.
/// </para>
/// </summary>
internal static class DeviceEventClassifier
{
    /// <summary>The firmware's message type for "I have just connected".</summary>
    public const string OnlineType = "online";

    /// <summary>The firmware's message type for "I am going away, and why".</summary>
    public const string OfflineType = "offline";

    /// <summary>The firmware's message type for "I have just woken from deep sleep, and why".</summary>
    public const string WakeType = "wake";

    /// <summary>The firmware's message type for "my motion-wake state machine moved on".</summary>
    public const string MotionType = "motion";

    /// <summary>Device offline reason → stored event. Every key is a word the firmware sends.</summary>
    private static readonly Dictionary<string, DeviceEventClassification> s_offlineReasons =
        new Dictionary<string, DeviceEventClassification>(StringComparer.Ordinal)
        {
            ["sleep"] = new DeviceEventClassification(
                DeviceEventKindNames.Offline, DeviceEventReasonNames.Sleep, DeviceEventSeverityNames.Normal),
            // Sleeping in standby with motion wake armed: the car was found parked.
            ["sleep_no_motion"] = new DeviceEventClassification(
                DeviceEventKindNames.Offline, DeviceEventReasonNames.SleepNoMotion, DeviceEventSeverityNames.Normal),
            ["power_off"] = new DeviceEventClassification(
                DeviceEventKindNames.Offline, DeviceEventReasonNames.PowerOff, DeviceEventSeverityNames.Normal),
            ["battery_low"] = new DeviceEventClassification(
                DeviceEventKindNames.Offline, DeviceEventReasonNames.BatteryLow, DeviceEventSeverityNames.Alert),
            ["error"] = new DeviceEventClassification(
                DeviceEventKindNames.Offline, DeviceEventReasonNames.Error, DeviceEventSeverityNames.Error),
            // The Last Will: the broker gave up on a session that never said goodbye.
            // An error rather than an alert because nothing planned ends this way — a
            // crash, a power cut or a link that died mid-session.
            ["connection_lost"] = new DeviceEventClassification(
                DeviceEventKindNames.Offline, DeviceEventReasonNames.ConnectionLost, DeviceEventSeverityNames.Error),
        };

    /// <summary>
    /// Chip reset cause → stored restart event. Causes missing from this table —
    /// DEEPSLEEP (every ordinary wake), EXT (the reset button), SDIO, UNKNOWN — are not
    /// worth a row: they update the device's online time and nothing else.
    /// </summary>
    private static readonly Dictionary<string, DeviceEventClassification> s_restartCauses =
        new Dictionary<string, DeviceEventClassification>(StringComparer.Ordinal)
        {
            // Power applied from nothing. Normal on its own — a first install, a pack
            // swapped — but worth recording, because after a flat battery it is the
            // only sign the device came back.
            ["POWERON"] = new DeviceEventClassification(
                DeviceEventKindNames.Restart, DeviceEventReasonNames.PowerOn, DeviceEventSeverityNames.Normal),
            // The supply sagged below the brown-out detector: a dying pack or a bad
            // connection, not a firmware fault.
            ["BROWNOUT"] = new DeviceEventClassification(
                DeviceEventKindNames.Restart, DeviceEventReasonNames.PowerLoss, DeviceEventSeverityNames.Alert),
            ["PANIC"] = new DeviceEventClassification(
                DeviceEventKindNames.Restart, DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error),
            ["INT_WDT"] = new DeviceEventClassification(
                DeviceEventKindNames.Restart, DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error),
            ["TASK_WDT"] = new DeviceEventClassification(
                DeviceEventKindNames.Restart, DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error),
            ["WDT"] = new DeviceEventClassification(
                DeviceEventKindNames.Restart, DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error),
            // The firmware never calls esp_restart() itself, so a software reset is
            // something going wrong somewhere it did not expect.
            ["SW"] = new DeviceEventClassification(
                DeviceEventKindNames.Restart, DeviceEventReasonNames.Crash, DeviceEventSeverityNames.Error),
        };

    /// <summary>
    /// Device wake cause → stored wake event. All normal: waking is what a tracker that
    /// sleeps between reports does, whichever source fired.
    /// </summary>
    private static readonly Dictionary<string, DeviceEventClassification> s_wakeCauses =
        new Dictionary<string, DeviceEventClassification>(StringComparer.Ordinal)
        {
            ["timer"] = new DeviceEventClassification(
                DeviceEventKindNames.Wake, DeviceEventReasonNames.Timer, DeviceEventSeverityNames.Normal),
            ["accelerometer"] = new DeviceEventClassification(
                DeviceEventKindNames.Wake, DeviceEventReasonNames.Accelerometer, DeviceEventSeverityNames.Normal),
            ["power_switch"] = new DeviceEventClassification(
                DeviceEventKindNames.Wake, DeviceEventReasonNames.PowerSwitch, DeviceEventSeverityNames.Normal),
        };

    /// <summary>
    /// Device motion step → stored motion event, one per transition of
    /// <c>ESP32/src/motion/MotionTracker</c>. All normal: they describe how the car is
    /// being used, never a fault.
    /// </summary>
    private static readonly Dictionary<string, DeviceEventClassification> s_motionSteps =
        new Dictionary<string, DeviceEventClassification>(StringComparer.Ordinal)
        {
            ["checking"] = new DeviceEventClassification(
                DeviceEventKindNames.Motion, DeviceEventReasonNames.Checking, DeviceEventSeverityNames.Normal),
            ["activity"] = new DeviceEventClassification(
                DeviceEventKindNames.Motion, DeviceEventReasonNames.Activity, DeviceEventSeverityNames.Normal),
            ["motion_on"] = new DeviceEventClassification(
                DeviceEventKindNames.Motion, DeviceEventReasonNames.MotionOn, DeviceEventSeverityNames.Normal),
            ["moving"] = new DeviceEventClassification(
                DeviceEventKindNames.Motion, DeviceEventReasonNames.Moving, DeviceEventSeverityNames.Normal),
            ["no_motion"] = new DeviceEventClassification(
                DeviceEventKindNames.Motion, DeviceEventReasonNames.NoMotion, DeviceEventSeverityNames.Normal),
            ["stopped"] = new DeviceEventClassification(
                DeviceEventKindNames.Motion, DeviceEventReasonNames.Stopped, DeviceEventSeverityNames.Normal),
            ["motion_off"] = new DeviceEventClassification(
                DeviceEventKindNames.Motion, DeviceEventReasonNames.MotionOff, DeviceEventSeverityNames.Normal),
        };

    /// <summary>Classifies a device's offline reason.</summary>
    /// <param name="deviceReason">The firmware's word, e.g. <c>battery_low</c>.</param>
    /// <param name="classification">The stored event when the method returns true.</param>
    /// <returns>False for a word the firmware is not known to send.</returns>
    public static bool TryClassifyOffline(
        string deviceReason,
        [NotNullWhen(true)] out DeviceEventClassification? classification)
    {
        return s_offlineReasons.TryGetValue(deviceReason, out classification);
    }

    /// <summary>Classifies a device's wake cause.</summary>
    /// <param name="deviceReason">The firmware's word, e.g. <c>accelerometer</c>.</param>
    /// <param name="classification">The stored event when the method returns true.</param>
    /// <returns>False for a word the firmware is not known to send.</returns>
    public static bool TryClassifyWake(
        string deviceReason,
        [NotNullWhen(true)] out DeviceEventClassification? classification)
    {
        return s_wakeCauses.TryGetValue(deviceReason, out classification);
    }

    /// <summary>Classifies a step of the device's motion-wake state machine.</summary>
    /// <param name="deviceReason">The firmware's word, e.g. <c>no_motion</c>.</param>
    /// <param name="classification">The stored event when the method returns true.</param>
    /// <returns>False for a word the firmware is not known to send.</returns>
    public static bool TryClassifyMotion(
        string deviceReason,
        [NotNullWhen(true)] out DeviceEventClassification? classification)
    {
        return s_motionSteps.TryGetValue(deviceReason, out classification);
    }

    /// <summary>Classifies the reset cause carried by an online message.</summary>
    /// <param name="resetReason">The chip's reset cause, e.g. <c>PANIC</c>, or null.</param>
    /// <returns>The restart event to store, or null when the cause does not warrant one.</returns>
    public static DeviceEventClassification? ClassifyRestart(string? resetReason)
    {
        if (resetReason is null)
        {
            return null;
        }

        return s_restartCauses.TryGetValue(resetReason, out DeviceEventClassification? classification)
            ? classification
            : null;
    }
}
