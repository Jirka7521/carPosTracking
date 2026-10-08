namespace CarPosAPI.Dtos;

/// <summary>
/// The remote settings, with no version or authorship around them: the seven STANDBY
/// values and the motion block (the motion-wake parameters plus a full MOVING copy of
/// the seven).
///
/// Factored out because the same values appear in three places — the revision
/// currently published, the (possibly older) revision a device is actually running,
/// and every entry in the history list — and the dashboard diffs them against each
/// other field by field. One shape means the diff is written once.
///
/// <para>
/// Flat on purpose: this is the dashboard's camelCase view, where a form with twelve
/// more inputs is easier to bind and to diff than a nested object. The device-facing,
/// nested shape is <see cref="DeviceMotionDocumentDto"/>; <c>DeviceMotionDocumentFactory</c>
/// is the one place that translates between the two.
/// </para>
/// </summary>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep and power the modem down between reports.</param>
/// <param name="FixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle.</param>
/// <param name="QueueMaxFixes">How many undelivered fixes the SD queue may hold.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a rejected fix.</param>
/// <param name="RetryMaxAgeHours">Hours after which a still-rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">How often an awake device re-asks the broker for this document.</param>
/// <param name="MotionEnabled">Whether motion wake is on; off, the other motion values are carried but inert.</param>
/// <param name="MotionThresholdMg">Accelerometer wake threshold in milli-g; the ADXL345 compares in 62.5 mg steps and the firmware rounds to the nearest.</param>
/// <param name="MotionSpeedKmph">A fix counts as moving when its GNSS speed is strictly above this, in km/h.</param>
/// <param name="MotionWakeWaitSeconds">How long a wake may look for a moving fix before going back to sleep.</param>
/// <param name="MotionStopWaitSeconds">How long after the last moving fix the device stays in moving mode.</param>
/// <param name="MovingIntervalSeconds">Seconds between position reports while moving.</param>
/// <param name="MovingSleepBetween">Deep-sleep and power the modem down between reports while moving.</param>
/// <param name="MovingFixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle, while moving.</param>
/// <param name="MovingQueueMaxFixes">Undelivered-fix queue cap while moving. The device applies the LARGER of this and the standby cap.</param>
/// <param name="MovingRetryIntervalHours">Hours between attempts on a rejected fix, while moving.</param>
/// <param name="MovingRetryMaxAgeHours">Hours after which a still-rejected fix is abandoned, while moving; 0 = never. The device applies the more lenient of this and the standby age.</param>
/// <param name="MovingConfigCheckSeconds">How often an awake device re-asks the broker for this document, while moving.</param>
public sealed record DeviceConfigValuesDto(
    int IntervalSeconds,
    bool SleepBetween,
    int FixTimeoutSeconds,
    int QueueMaxFixes,
    int RetryIntervalHours,
    int RetryMaxAgeHours,
    int ConfigCheckSeconds,
    bool MotionEnabled,
    int MotionThresholdMg,
    int MotionSpeedKmph,
    int MotionWakeWaitSeconds,
    int MotionStopWaitSeconds,
    int MovingIntervalSeconds,
    bool MovingSleepBetween,
    int MovingFixTimeoutSeconds,
    int MovingQueueMaxFixes,
    int MovingRetryIntervalHours,
    int MovingRetryMaxAgeHours,
    int MovingConfigCheckSeconds);
