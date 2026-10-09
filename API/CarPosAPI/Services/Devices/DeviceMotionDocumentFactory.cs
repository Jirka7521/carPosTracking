using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Builds the nested <c>motion</c> object of the device-facing settings document from
/// any of the three shapes the eight motion values are held in.
///
/// <para>
/// The same block has to be assembled at every place a document is built — the
/// retained config, each profile and the override in the schedule bundle, the
/// reconnect sweep, a revision just saved. Written out by hand at each one it would be
/// five copies of an eight-argument constructor call, and the next setting added to
/// the motion block would be missed in exactly one of them: a device then receives a
/// document that disagrees with its own database row, with nothing anywhere to say so.
/// One mapper means the field-to-key mapping exists once.
/// </para>
///
/// <para>
/// It lives in the services layer rather than on the DTO because two of its inputs are
/// EF entities, and a DTO must never know about those. Stateless and pure, so it is
/// safe to call from a singleton (<c>MqttConfigPublisher</c>) as well as from scoped
/// services.
/// </para>
/// </summary>
internal static class DeviceMotionDocumentFactory
{
    /// <summary>Builds the motion block for a stored revision.</summary>
    /// <param name="configVersion">The revision supplying the values.</param>
    /// <returns>The block to place after <c>config_check_s</c>.</returns>
    public static DeviceMotionDocumentDto Create(DeviceConfigVersion configVersion)
    {
        ArgumentNullException.ThrowIfNull(configVersion);

        return Assemble(
            configVersion.MotionEnabled,
            configVersion.MotionThresholdMg,
            configVersion.MotionSpeedKmph,
            configVersion.MotionWakeWaitSeconds,
            configVersion.MotionStopWaitSeconds,
            configVersion.MovingIntervalSeconds,
            configVersion.MovingSleepBetween,
            configVersion.MovingFixTimeoutSeconds);
    }

    /// <summary>Builds the motion block for a schedule profile.</summary>
    /// <param name="profile">The profile supplying the values.</param>
    /// <returns>The block to place after <c>config_check_s</c>.</returns>
    public static DeviceMotionDocumentDto Create(DeviceConfigProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return Assemble(
            profile.MotionEnabled,
            profile.MotionThresholdMg,
            profile.MotionSpeedKmph,
            profile.MotionWakeWaitSeconds,
            profile.MotionStopWaitSeconds,
            profile.MovingIntervalSeconds,
            profile.MovingSleepBetween,
            profile.MovingFixTimeoutSeconds);
    }

    /// <summary>Builds the motion block for the dashboard's flat settings shape.</summary>
    /// <param name="values">The settings supplying the values.</param>
    /// <returns>The block to place after <c>config_check_s</c>.</returns>
    public static DeviceMotionDocumentDto Create(DeviceConfigValuesDto values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return Assemble(
            values.MotionEnabled,
            values.MotionThresholdMg,
            values.MotionSpeedKmph,
            values.MotionWakeWaitSeconds,
            values.MotionStopWaitSeconds,
            values.MovingIntervalSeconds,
            values.MovingSleepBetween,
            values.MovingFixTimeoutSeconds);
    }

    /// <summary>
    /// The one place the eight values meet the wire shape, so the three overloads above
    /// can differ only in where they read from.
    /// </summary>
    /// <param name="enabled">Whether motion wake is on.</param>
    /// <param name="thresholdMg">Wake threshold in milli-g.</param>
    /// <param name="speedKmph">Speed above which a fix counts as moving.</param>
    /// <param name="wakeWaitSeconds">How long a wake looks for a moving fix.</param>
    /// <param name="stopWaitSeconds">How long after the last moving fix the device stays in moving mode.</param>
    /// <param name="movingIntervalSeconds">Moving report interval.</param>
    /// <param name="movingSleepBetween">Moving deep-sleep flag.</param>
    /// <param name="movingFixTimeoutSeconds">Moving GNSS acquire budget.</param>
    /// <returns>The block.</returns>
    private static DeviceMotionDocumentDto Assemble(
        bool enabled,
        int thresholdMg,
        int speedKmph,
        int wakeWaitSeconds,
        int stopWaitSeconds,
        int movingIntervalSeconds,
        bool movingSleepBetween,
        int movingFixTimeoutSeconds)
    {
        return new DeviceMotionDocumentDto(
            enabled,
            thresholdMg,
            speedKmph,
            wakeWaitSeconds,
            stopWaitSeconds,
            new DeviceModeDocumentDto(
                movingIntervalSeconds,
                movingSleepBetween,
                movingFixTimeoutSeconds));
    }
}
