using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Creates a profile, or replaces one wholesale. Every field is required, for the same
/// reason as <see cref="UpdateDeviceConfigRequestDto"/>: a client that forgets one is
/// told, rather than silently keeping a value nobody chose.
///
/// <para>
/// The <c>[Range]</c> bounds are the same <see cref="DeviceConfigRules"/> constants the
/// manual settings form validates against — a profile is not a lesser kind of
/// configuration, and a value the API would reject on the settings panel must not
/// become reachable by routing it through a schedule.
/// </para>
///
/// <para>
/// One DTO for both create and update because the operations differ only in whether a
/// row already exists. Two near-identical records would be two places to add the next
/// setting to, and one of them would be missed.
/// </para>
/// </summary>
/// <param name="Name">What to call it. Unique per device, case-insensitively.</param>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep and power the modem down between reports.</param>
/// <param name="FixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle.</param>
/// <param name="QueueMaxFixes">How many undelivered fixes the SD queue may hold, in either mode.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a fix this API rejected, in either mode.</param>
/// <param name="RetryMaxAgeHours">Hours after which a still-rejected fix is abandoned, in either mode; 0 = never.</param>
/// <param name="ConfigCheckSeconds">How often an awake device asks the broker to re-send its configuration, in either mode.</param>
/// <param name="MotionEnabled">Turns motion wake on; interval, sleep and fix timeout above are then the STANDBY set and the <c>Moving*</c> values the set used while driving.</param>
/// <param name="MotionThresholdMg">Accelerometer wake threshold in milli-g; the ADXL345 compares in 62.5 mg steps and the firmware rounds to the nearest (63 mg = step 1, the most sensitive).</param>
/// <param name="MotionSpeedKmph">A fix counts as moving when its GNSS speed is strictly above this, in km/h; not 0, because a parked receiver reports 0-3 km/h of jitter.</param>
/// <param name="MotionWakeWaitSeconds">How long a wake may look for a moving fix before going back to sleep.</param>
/// <param name="MotionStopWaitSeconds">How long after the last moving fix the device stays in moving mode.</param>
/// <param name="MovingIntervalSeconds">Seconds between position reports while moving.</param>
/// <param name="MovingSleepBetween">Deep-sleep and power the modem down between reports while moving.</param>
/// <param name="MovingFixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle, while moving.</param>
public sealed record SaveConfigProfileRequestDto(
    [Required]
    [StringLength(
        ScheduleRules.MaxProfileNameLength,
        MinimumLength = ScheduleRules.MinProfileNameLength)]
    string Name,

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
    int MovingFixTimeoutSeconds);
