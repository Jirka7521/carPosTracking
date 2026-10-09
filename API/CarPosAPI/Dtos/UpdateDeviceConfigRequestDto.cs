using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// A full replacement of a device's settings — not a patch. Every field is required,
/// so a client that forgets one is told (400) rather than silently keeping a value it
/// did not mean to keep. Saving inserts a new revision; it never edits an old one.
///
/// <para>
/// The <c>[Range]</c> bounds come from <see cref="DeviceConfigRules"/> and mirror the
/// firmware's clamps exactly. Rejecting here rather than clamping is deliberate: a
/// person at a dashboard can be shown what is wrong, whereas the device — which has
/// nobody to ask — clamps and carries on.
/// </para>
/// </summary>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">
/// Deep-sleep and power the modem down between reports. Saves a great deal of battery
/// above a few minutes, at the cost of a cold GNSS fix and a fresh TLS handshake every
/// cycle — validation cannot express that trade-off, so the UI explains it instead.
/// </param>
/// <param name="FixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle.</param>
/// <param name="QueueMaxFixes">How many undelivered fixes the SD queue may hold, in either mode.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a fix this API rejected, in either mode.</param>
/// <param name="RetryMaxAgeHours">Hours after which a still-rejected fix is abandoned, in either mode; 0 = never.</param>
/// <param name="ConfigCheckSeconds">
/// How often an awake device asks the broker to re-send this document. A backstop
/// only — a saved change normally reaches the device by push within a second. It has
/// no effect at all while <paramref name="SleepBetween"/> is on, because a sleeping
/// device re-reads its configuration on every wake.
/// </param>
/// <param name="MotionEnabled">
/// Turns motion wake on. Interval, sleep and fix timeout above are then the STANDBY set
/// (the car is parked) and the <c>Moving*</c> values below are the set the device runs
/// while it is driving; the queue, retry and re-check values above apply in both.
/// Off, every other motion value is stored but inert.
/// </param>
/// <param name="MotionThresholdMg">
/// Accelerometer wake threshold, in milli-g. The ADXL345 compares in 62.5 mg steps and
/// the firmware rounds this to the nearest one, so 63 mg is step 1 — the most sensitive
/// setting there is, and therefore the floor.
/// </param>
/// <param name="MotionSpeedKmph">
/// A fix counts as moving when its GNSS speed is strictly above this. Not 0: a parked
/// receiver reports 0-3 km/h of jitter, which would keep the device awake.
/// </param>
/// <param name="MotionWakeWaitSeconds">How long a wake may look for a moving fix before going back to sleep.</param>
/// <param name="MotionStopWaitSeconds">How long after the last moving fix the device stays in moving mode.</param>
/// <param name="MovingIntervalSeconds">Seconds between position reports while moving.</param>
/// <param name="MovingSleepBetween">Deep-sleep and power the modem down between reports while moving.</param>
/// <param name="MovingFixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle, while moving.</param>
/// <param name="AcknowledgeOverride">
/// The caller understands that on a device with an <em>enabled schedule</em> this save
/// is temporary: it holds only until the next scheduled switch, which then reasserts
/// its profile. Without it such a save is refused (400).
///
/// <para>
/// A server-side gate rather than a dashboard-only confirmation, because the surprise
/// it prevents is severe and silent — settings reverting hours later, with nothing on
/// screen to explain why — and because the dashboard is not the only thing that can
/// reach this endpoint. A client that has not been updated gets an error telling it
/// what it did not know, which is strictly better than getting the surprise.
/// </para>
///
/// <para>
/// Defaulted, and so optional on the wire: a device with no schedule ignores it
/// entirely, and that is every device until somebody sets one up.
/// </para>
/// </param>
public sealed record UpdateDeviceConfigRequestDto(
    [Required]
    [Range(DeviceConfigRules.MinIntervalSeconds, DeviceConfigRules.MaxIntervalSeconds)]
    int IntervalSeconds,

    [Required]
    bool SleepBetween,

    [Required]
    [Range(DeviceConfigRules.MinFixTimeoutSeconds, DeviceConfigRules.MaxFixTimeoutSeconds)]
    int FixTimeoutSeconds,

    [Required]
    [Range(DeviceConfigRules.MinQueueMaxFixes, DeviceConfigRules.MaxQueueMaxFixes)]
    int QueueMaxFixes,

    [Required]
    [Range(DeviceConfigRules.MinRetryIntervalHours, DeviceConfigRules.MaxRetryIntervalHours)]
    int RetryIntervalHours,

    [Required]
    [Range(DeviceConfigRules.MinRetryMaxAgeHours, DeviceConfigRules.MaxRetryMaxAgeHours)]
    int RetryMaxAgeHours,

    [Required]
    [Range(DeviceConfigRules.MinConfigCheckSeconds, DeviceConfigRules.MaxConfigCheckSeconds)]
    int ConfigCheckSeconds,

    [Required]
    bool MotionEnabled,

    [Required]
    [Range(DeviceConfigRules.MinMotionThresholdMg, DeviceConfigRules.MaxMotionThresholdMg)]
    int MotionThresholdMg,

    [Required]
    [Range(DeviceConfigRules.MinMotionSpeedKmph, DeviceConfigRules.MaxMotionSpeedKmph)]
    int MotionSpeedKmph,

    [Required]
    [Range(DeviceConfigRules.MinMotionWakeWaitSeconds, DeviceConfigRules.MaxMotionWakeWaitSeconds)]
    int MotionWakeWaitSeconds,

    [Required]
    [Range(DeviceConfigRules.MinMotionStopWaitSeconds, DeviceConfigRules.MaxMotionStopWaitSeconds)]
    int MotionStopWaitSeconds,

    [Required]
    [Range(DeviceConfigRules.MinIntervalSeconds, DeviceConfigRules.MaxIntervalSeconds)]
    int MovingIntervalSeconds,

    [Required]
    bool MovingSleepBetween,

    [Required]
    [Range(DeviceConfigRules.MinFixTimeoutSeconds, DeviceConfigRules.MaxFixTimeoutSeconds)]
    int MovingFixTimeoutSeconds,

    bool AcknowledgeOverride = false);
