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
}
