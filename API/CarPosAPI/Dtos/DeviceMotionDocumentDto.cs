using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The <c>motion</c> object of the settings document: the motion-wake parameters plus
/// a full MOVING copy of the seven runtime settings. It sits after
/// <c>config_check_s</c> in <see cref="DeviceConfigDocumentDto"/>,
/// <see cref="ScheduleBundleProfileDto"/> and <see cref="ScheduleBundleOverrideDto"/>
/// alike, so a schedule profile switches the whole motion block along with the
/// standby values.
///
/// <para>
/// With motion wake on, the device runs one of two sets: the top-level seven keys
/// (STANDBY) while the vehicle is parked, and <see cref="Moving"/> while it is driving.
/// Off, the device only ever runs the top-level set and everything in here is inert —
/// which is why the factory default is <c>enabled: false</c> and why adding this block
/// changes nothing a device does until somebody turns it on.
/// </para>
///
/// <para>
/// The field names are fixed by the firmware's <c>SettingsCodec</c>, which decodes this
/// object for the MQTT message, the SD-card cache and every schedule profile alike, so
/// they must match character for character. Hence the explicit
/// <see cref="JsonPropertyNameAttribute"/> on every member. Build one with
/// <c>DeviceMotionDocumentFactory</c> rather than by hand at each publisher.
/// </para>
/// </summary>
/// <param name="Enabled">Whether motion wake is on.</param>
/// <param name="ThresholdMg">
/// Accelerometer wake threshold, in milli-g. The ADXL345 compares in 62.5 mg steps and
/// the firmware rounds this to the nearest one (63 mg = step 1, the most sensitive).
/// </param>
/// <param name="SpeedKmph">
/// A fix counts as moving when its GNSS speed is strictly above this, in km/h. Not 0:
/// a parked receiver reports 0-3 km/h of jitter.
/// </param>
/// <param name="WakeWaitSeconds">How long a wake may look for a moving fix before going back to sleep.</param>
/// <param name="StopWaitSeconds">How long after the last moving fix the device stays in moving mode.</param>
/// <param name="Moving">The settings the device runs while it is moving.</param>
public sealed record DeviceMotionDocumentDto(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("threshold_mg")] int ThresholdMg,
    [property: JsonPropertyName("speed_kmph")] int SpeedKmph,
    [property: JsonPropertyName("wake_wait_s")] int WakeWaitSeconds,
    [property: JsonPropertyName("stop_wait_s")] int StopWaitSeconds,
    [property: JsonPropertyName("moving")] DeviceModeDocumentDto Moving);
