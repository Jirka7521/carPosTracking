using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Ingest;

namespace CarPosAPI.Tests;

/// <summary>
/// Status-message validation: strict on who sent it and what it means, lenient on the
/// optional fields — a bad battery figure or timestamp is dropped to null and the event
/// is kept, because losing a "battery low" to a decoration field would defeat it.
/// </summary>
public sealed class DeviceStatusValidatorTests
{
    private const string TopicDeviceId = "GNSS01";

    /// <summary>Fixed "now" so the timestamp window is deterministic.</summary>
    private static readonly DateTime s_utcNow = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A routine "going to sleep" message that passes every check.</summary>
    /// <returns>A fresh valid payload; tests override single fields with <c>with</c>.</returns>
    private static DeviceStatusPayloadDto ValidOffline()
    {
        return new DeviceStatusPayloadDto(
            Device: TopicDeviceId,
            Type: "offline",
            Reason: "sleep",
            TimeUtc: "2026-10-09T11:59:30Z",
            BatteryPct: 57,
            SleepSeconds: 300);
    }

    /// <summary>Creates the validator with default options.</summary>
    /// <returns>The validator under test.</returns>
    private static DeviceStatusValidator CreateValidator()
    {
        return new DeviceStatusValidator(Microsoft.Extensions.Options.Options.Create(new IngestOptions()));
    }

    [Fact]
    public void AcceptsAndClassifiesARoutineSleep()
    {
        bool valid = CreateValidator().TryValidate(
            ValidOffline(), TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason reason);

        Assert.True(valid);
        Assert.Equal(DeviceStatusRejectReason.None, reason);
        Assert.NotNull(status);
        Assert.False(status.IsOnline);
        Assert.NotNull(status.Event);
        Assert.Equal(DeviceEventKindNames.Offline, status.Event.Kind);
        Assert.Equal(DeviceEventReasonNames.Sleep, status.Event.Reason);
        Assert.Equal(DeviceEventSeverityNames.Normal, status.Event.Severity);
        Assert.Equal(57, status.BatteryPct);
        Assert.Equal(300, status.SleepSeconds);
        Assert.Equal(new DateTime(2026, 10, 9, 11, 59, 30, DateTimeKind.Utc), status.DeviceTimeUtc);
        // Npgsql demands Utc kind for timestamptz.
        Assert.Equal(DateTimeKind.Utc, status.DeviceTimeUtc!.Value.Kind);
    }

    [Fact]
    public void AcceptsTheLastWillWithNothingButAReason()
    {
        // Exactly what the firmware seals as its will: no time, no battery, no sleep.
        DeviceStatusPayloadDto will = new DeviceStatusPayloadDto(TopicDeviceId, "offline", "connection_lost");

        bool valid = CreateValidator().TryValidate(
            will, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        Assert.NotNull(status?.Event);
        Assert.Equal(DeviceEventReasonNames.ConnectionLost, status.Event.Reason);
        Assert.Equal(DeviceEventSeverityNames.Error, status.Event.Severity);
        Assert.Null(status.DeviceTimeUtc);
        Assert.Null(status.BatteryPct);
        Assert.Null(status.SleepSeconds);
    }

    [Fact]
    public void RejectsDeviceMismatch()
    {
        // Sealed for GNSS01 but replayed onto GNSS02's status topic.
        bool valid = CreateValidator().TryValidate(
            ValidOffline(), "GNSS02", s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason reason);

        Assert.False(valid);
        Assert.Null(status);
        Assert.Equal(DeviceStatusRejectReason.DeviceMismatch, reason);
    }

    // The expected reason travels by name: the enum is internal, and a public test
    // method cannot take it as a parameter.
    [Theory]
    [InlineData(null, "offline", "sleep", nameof(DeviceStatusRejectReason.MissingField))]
    [InlineData(TopicDeviceId, null, "sleep", nameof(DeviceStatusRejectReason.MissingField))]
    [InlineData(TopicDeviceId, "offline", null, nameof(DeviceStatusRejectReason.MissingField))]
    [InlineData(TopicDeviceId, "rebooting", "sleep", nameof(DeviceStatusRejectReason.UnknownType))]
    [InlineData(TopicDeviceId, "offline", "napping", nameof(DeviceStatusRejectReason.UnknownReason))]
    // Case matters: the firmware's vocabulary is exact, and a near miss is not it.
    [InlineData(TopicDeviceId, "offline", "Sleep", nameof(DeviceStatusRejectReason.UnknownReason))]
    [InlineData(TopicDeviceId, "wake", null, nameof(DeviceStatusRejectReason.MissingField))]
    [InlineData(TopicDeviceId, "wake", "alarm_clock", nameof(DeviceStatusRejectReason.UnknownReason))]
    [InlineData(TopicDeviceId, "motion", null, nameof(DeviceStatusRejectReason.MissingField))]
    [InlineData(TopicDeviceId, "motion", "flying", nameof(DeviceStatusRejectReason.UnknownReason))]
    // A word from another type's vocabulary is still unknown here.
    [InlineData(TopicDeviceId, "motion", "timer", nameof(DeviceStatusRejectReason.UnknownReason))]
    public void RejectsWhatMakesAMessageMeaningless(
        string? device,
        string? type,
        string? reasonWord,
        string expected)
    {
        DeviceStatusPayloadDto payload = new DeviceStatusPayloadDto(device, type, reasonWord);

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason reason);

        Assert.False(valid);
        Assert.Null(status);
        Assert.Equal(expected, reason.ToString());
    }

    [Fact]
    public void OnlineWithoutAReasonIsValidAndRecordsNoEvent()
    {
        // The ordinary online message after a deep-sleep wake: it moves the device's
        // online time and is not worth a history row.
        DeviceStatusPayloadDto online = new DeviceStatusPayloadDto(TopicDeviceId, "online", ResetReason: "DEEPSLEEP");

        bool valid = CreateValidator().TryValidate(
            online, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        Assert.NotNull(status);
        Assert.True(status.IsOnline);
        Assert.Null(status.Event);
    }

    [Fact]
    public void OnlineAfterAPanicRecordsACrash()
    {
        DeviceStatusPayloadDto online = new DeviceStatusPayloadDto(TopicDeviceId, "online", ResetReason: "PANIC");

        bool valid = CreateValidator().TryValidate(
            online, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        Assert.True(status!.IsOnline);
        Assert.NotNull(status.Event);
        Assert.Equal(DeviceEventKindNames.Restart, status.Event.Kind);
        Assert.Equal(DeviceEventReasonNames.Crash, status.Event.Reason);
        Assert.Equal(DeviceEventSeverityNames.Error, status.Event.Severity);
    }

    [Fact]
    public void AcceptsAWakeWithItsCauseAndTime()
    {
        DeviceStatusPayloadDto wake = new DeviceStatusPayloadDto(
            TopicDeviceId, "wake", "accelerometer", TimeUtc: "2026-10-09T08:15:00Z");

        bool valid = CreateValidator().TryValidate(
            wake, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        // A wake says nothing about the connection - it may have come off the card
        // hours late - so it must not move the device's online time.
        Assert.False(status!.IsOnline);
        Assert.NotNull(status.Event);
        Assert.Equal(DeviceEventKindNames.Wake, status.Event.Kind);
        Assert.Equal(DeviceEventReasonNames.Accelerometer, status.Event.Reason);
        // Hours old, as an event kept on the card while out of range would be.
        Assert.Equal(new DateTime(2026, 10, 9, 8, 15, 0, DateTimeKind.Utc), status.DeviceTimeUtc);
    }

    [Fact]
    public void AcceptsAMotionStep()
    {
        DeviceStatusPayloadDto step = new DeviceStatusPayloadDto(TopicDeviceId, "motion", "no_motion");

        bool valid = CreateValidator().TryValidate(
            step, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        Assert.False(status!.IsOnline);
        Assert.Equal(DeviceEventKindNames.Motion, status.Event!.Kind);
        Assert.Equal(DeviceEventReasonNames.NoMotion, status.Event.Reason);
        Assert.Equal(DeviceEventSeverityNames.Normal, status.Event.Severity);
    }

    [Fact]
    public void ClassifiesASleepBecauseParked()
    {
        DeviceStatusPayloadDto payload = ValidOffline() with { Reason = "sleep_no_motion" };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        Assert.Equal(DeviceEventKindNames.Offline, status!.Event!.Kind);
        Assert.Equal(DeviceEventReasonNames.SleepNoMotion, status.Event.Reason);
        Assert.Equal(300, status.SleepSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void DropsAnOutOfRangeBatteryButKeepsTheEvent(int batteryPct)
    {
        DeviceStatusPayloadDto payload = ValidOffline() with { Reason = "battery_low", BatteryPct = batteryPct };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        Assert.Null(status!.BatteryPct);
        Assert.Equal(DeviceEventReasonNames.BatteryLow, status.Event!.Reason);
    }

    [Fact]
    public void KeepsTheChargingSentinel()
    {
        DeviceStatusPayloadDto payload = ValidOffline() with { BatteryPct = 0 };

        CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.Equal(0, status!.BatteryPct);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(DeviceConfigRules.MaxIntervalSeconds + 1)]
    public void DropsAnImplausibleSleep(int sleepSeconds)
    {
        DeviceStatusPayloadDto payload = ValidOffline() with { SleepSeconds = sleepSeconds };

        CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.Null(status!.SleepSeconds);
    }

    [Theory]
    // Not the firmware's exact shape.
    [InlineData("2026-10-09 11:59:30")]
    [InlineData("2026-10-09T11:59:30.000Z")]
    // Too far in the future, beyond the configured skew.
    [InlineData("2026-10-09T13:00:00Z")]
    // Older than even an event kept on the card plausibly is.
    [InlineData("2026-08-01T00:00:00Z")]
    public void DropsAnUnusableDeviceTime(string timeUtc)
    {
        DeviceStatusPayloadDto payload = ValidOffline() with { TimeUtc = timeUtc };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.True(valid);
        Assert.Null(status!.DeviceTimeUtc);
    }

    [Theory]
    [InlineData("gnss_init", "gnss_init")]
    [InlineData("GNSS_INIT", null)]
    [InlineData("gnss init", null)]
    [InlineData("a_detail_code_that_is_much_too_long_x", null)]
    public void KeepsOnlyWellFormedDetailCodes(string detail, string? expected)
    {
        DeviceStatusPayloadDto payload = ValidOffline() with { Reason = "error", Detail = detail };

        CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedDeviceStatus? status, out DeviceStatusRejectReason _);

        Assert.Equal(expected, status!.Detail);
    }
}
