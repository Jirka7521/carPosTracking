namespace CarPosAPI.Dtos;

/// <summary>
/// The wire spellings of why a device event happened — stored in
/// <c>device_events.reason</c> and translated by the dashboard
/// (<c>device:events.reason.*</c>).
///
/// <para>
/// camelCase like every other value this API emits, which is why they differ from the
/// firmware's snake_case words (<c>power_off</c> arrives, <c>powerOff</c> is stored):
/// the device vocabulary stops at <c>Services.Ingest.DeviceEventClassifier</c>, which
/// is the one place that maps between the two.
/// </para>
/// </summary>
public static class DeviceEventReasonNames
{
    /// <summary>Offline: a planned deep sleep between reports. Normal.</summary>
    public const string Sleep = "sleep";

    /// <summary>
    /// Offline: a deep sleep because the car was found parked — motion wake is on and
    /// the accelerometer is armed to wake it. Normal.
    /// </summary>
    public const string SleepNoMotion = "sleepNoMotion";

    /// <summary>Offline: the operator switched the unit off. Normal.</summary>
    public const string PowerOff = "powerOff";

    /// <summary>Offline: the pack fell below the firmware's cut-off. An alert.</summary>
    public const string BatteryLow = "batteryLow";

    /// <summary>Offline: a fault the firmware caught itself (see the event's detail). An error.</summary>
    public const string Error = "error";

    /// <summary>
    /// Offline: the session died without a goodbye, and the broker published the
    /// device's Last Will on its behalf. An error.
    /// </summary>
    public const string ConnectionLost = "connectionLost";

    /// <summary>Restart: a cold power-on — power was applied from nothing. Normal.</summary>
    public const string PowerOn = "powerOn";

    /// <summary>Restart: the supply sagged until the chip browned out. An alert.</summary>
    public const string PowerLoss = "powerLoss";

    /// <summary>Restart: a panic, a watchdog or a software reset. An error.</summary>
    public const string Crash = "crash";

    /// <summary>Wake: the RTC timer — the regular wake for the next report. Normal.</summary>
    public const string Timer = "timer";

    /// <summary>Wake: the accelerometer — the car moved while the device slept. Normal.</summary>
    public const string Accelerometer = "accelerometer";

    /// <summary>Wake: the power switch was turned back on. Normal.</summary>
    public const string PowerSwitch = "powerSwitch";

    /// <summary>Motion: the boot began a check for movement (after any wake or a power-on). Normal.</summary>
    public const string Checking = "checking";

    /// <summary>Motion: the accelerometer tripped while the device was awake and parked — a check began. Normal.</summary>
    public const string Activity = "activity";

    /// <summary>Motion: a configuration switched motion wake on — a check began. Normal.</summary>
    public const string MotionOn = "motionOn";

    /// <summary>Motion: a fix faster than the speed threshold — the car is moving. Normal.</summary>
    public const string Moving = "moving";

    /// <summary>Motion: a check ended without a fast fix — the car is parked. Normal.</summary>
    public const string NoMotion = "noMotion";

    /// <summary>Motion: the car stood still for the whole stop window — the trip ended. Normal.</summary>
    public const string Stopped = "stopped";

    /// <summary>Motion: a configuration switched motion wake off. Normal.</summary>
    public const string MotionOff = "motionOff";
}
