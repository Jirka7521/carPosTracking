using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Ingest;

namespace CarPosAPI.Tests;

/// <summary>
/// Semantic validation edges: field presence, ranges (mirroring the DB CHECK
/// constraints), the exact firmware timestamp format, the acceptance window and
/// the topic-vs-payload device match that stops cross-device writes.
/// </summary>
public sealed class PositionValidatorTests
{
    private const string TopicDeviceId = "GNSS01";

    /// <summary>Fixed "now" so window tests are deterministic.</summary>
    private static readonly DateTime s_utcNow = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A payload that passes every check; tests override single fields.</summary>
    /// <returns>A fresh valid payload.</returns>
    private static PositionPayloadDto ValidPayload()
    {
        return new PositionPayloadDto(
            Device: TopicDeviceId,
            LatitudeDeg: 50.123456,
            LongitudeDeg: 14.654321,
            SpeedKmph: 42.5,
            AltitudeMeters: 231.4,
            TimeUtc: "2026-07-14T12:34:56Z");
    }

    /// <summary>Creates the validator with default options.</summary>
    /// <returns>The validator under test.</returns>
    private static PositionValidator CreateValidator()
    {
        return new PositionValidator(Microsoft.Extensions.Options.Options.Create(new IngestOptions()));
    }

    [Fact]
    public void AcceptsValidPayload()
    {
        bool valid = CreateValidator().TryValidate(
            ValidPayload(), TopicDeviceId, s_utcNow, out ValidatedPosition? position, out PositionRejectReason reason);

        Assert.True(valid);
        Assert.Equal(PositionRejectReason.None, reason);
        Assert.NotNull(position);
        Assert.Equal(TopicDeviceId, position.DeviceId);
        Assert.Equal(50.123456, position.Latitude);
        Assert.Equal(14.654321, position.Longitude);
        Assert.Equal(new DateTime(2026, 7, 14, 12, 34, 56, DateTimeKind.Utc), position.FixTimeUtc);
        // Npgsql demands Utc kind for timestamptz — the validator must guarantee it.
        Assert.Equal(DateTimeKind.Utc, position.FixTimeUtc.Kind);
    }

    [Fact]
    public void RejectsDeviceMismatch()
    {
        // Payload claims GNSS01 but arrived on GNSS02's topic — cross-device replay.
        bool valid = CreateValidator().TryValidate(
            ValidPayload(), "GNSS02", s_utcNow, out ValidatedPosition? position, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Null(position);
        Assert.Equal(PositionRejectReason.DeviceMismatch, reason);
    }

    [Fact]
    public void RejectsMissingField()
    {
        PositionPayloadDto payload = ValidPayload() with { SpeedKmph = null };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.MissingField, reason);
    }

    [Fact]
    public void RejectsNonFiniteNumbers()
    {
        PositionPayloadDto payload = ValidPayload() with { LatitudeDeg = double.NaN };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.NonFiniteNumber, reason);
    }

    [Theory]
    [InlineData(90.001)]
    [InlineData(-90.001)]
    public void RejectsLatitudeOutOfRange(double latitude)
    {
        PositionPayloadDto payload = ValidPayload() with { LatitudeDeg = latitude };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.LatitudeOutOfRange, reason);
    }

    [Theory]
    [InlineData(180.001)]
    [InlineData(-180.001)]
    public void RejectsLongitudeOutOfRange(double longitude)
    {
        PositionPayloadDto payload = ValidPayload() with { LongitudeDeg = longitude };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.LongitudeOutOfRange, reason);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1000.1)]
    public void RejectsSpeedOutOfRange(double speed)
    {
        PositionPayloadDto payload = ValidPayload() with { SpeedKmph = speed };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.SpeedOutOfRange, reason);
    }

    [Theory]
    [InlineData(-500.1)]
    [InlineData(10000.1)]
    public void RejectsAltitudeOutOfRange(double altitude)
    {
        PositionPayloadDto payload = ValidPayload() with { AltitudeMeters = altitude };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.AltitudeOutOfRange, reason);
    }

    [Theory]
    [InlineData("2026-07-14T12:34:56.123Z")]
    [InlineData("2026-07-14 12:34:56Z")]
    [InlineData("2026-07-14T12:34:56+00:00")]
    [InlineData("2026-07-14T12:34:56")]
    [InlineData("not-a-time")]
    public void RejectsNonFirmwareTimestampFormats(string timeUtc)
    {
        PositionPayloadDto payload = ValidPayload() with { TimeUtc = timeUtc };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.TimestampInvalid, reason);
    }

    [Fact]
    public void RejectsFixBeforeMinimumDate()
    {
        PositionPayloadDto payload = ValidPayload() with { TimeUtc = "2019-12-31T23:59:59Z" };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.TimestampOutOfWindow, reason);
    }

    [Fact]
    public void RejectsFixTooFarInTheFuture()
    {
        // Default skew allowance is 10 minutes; 11 minutes ahead must fail.
        PositionPayloadDto payload = ValidPayload() with { TimeUtc = "2026-07-19T12:11:00Z" };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.TimestampOutOfWindow, reason);
    }

    [Fact]
    public void AcceptsFixWithinFutureSkewAllowance()
    {
        PositionPayloadDto payload = ValidPayload() with { TimeUtc = "2026-07-19T12:09:00Z" };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? position, out PositionRejectReason _);

        Assert.True(valid);
        Assert.NotNull(position);
    }

    [Fact]
    public void AcceptsDaysOldBacklogFix()
    {
        // SD-card store-and-forward legitimately delivers old fixes.
        PositionPayloadDto payload = ValidPayload() with { TimeUtc = "2026-06-01T00:00:00Z" };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? position, out PositionRejectReason _);

        Assert.True(valid);
        Assert.NotNull(position);
    }

    [Fact]
    public void AbsentBatteryAndAccelBecomeNull()
    {
        // The base valid payload carries no sensor fields — that is not a rejection,
        // it is simply stored as null (older firmware / disabled sensors).
        bool valid = CreateValidator().TryValidate(
            ValidPayload(), TopicDeviceId, s_utcNow, out ValidatedPosition? position, out PositionRejectReason _);

        Assert.True(valid);
        Assert.NotNull(position);
        Assert.Null(position.BatteryPct);
        Assert.Null(position.AccelXG);
        Assert.Null(position.AccelYG);
        Assert.Null(position.AccelZG);
    }

    [Fact]
    public void CarriesBatteryAndAccelWhenPresent()
    {
        PositionPayloadDto payload = ValidPayload() with
        {
            BatteryPct = 87,
            AccelXG = 0.01,
            AccelYG = -0.02,
            AccelZG = 0.99,
        };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? position, out PositionRejectReason _);

        Assert.True(valid);
        Assert.NotNull(position);
        Assert.Equal(87, position.BatteryPct);
        Assert.Equal(0.01, position.AccelXG);
        Assert.Equal(-0.02, position.AccelYG);
        Assert.Equal(0.99, position.AccelZG);
    }

    [Fact]
    public void AcceptsChargingSentinelZeroBattery()
    {
        // 0 is a valid value: the firmware's "charging" sentinel, not out of range.
        PositionPayloadDto payload = ValidPayload() with { BatteryPct = 0 };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? position, out PositionRejectReason _);

        Assert.True(valid);
        Assert.NotNull(position);
        Assert.Equal(0, position.BatteryPct);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RejectsBatteryOutOfRange(int batteryPct)
    {
        PositionPayloadDto payload = ValidPayload() with { BatteryPct = batteryPct };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.BatteryOutOfRange, reason);
    }

    [Theory]
    [InlineData(16.1)]
    [InlineData(-16.1)]
    [InlineData(double.NaN)]
    public void RejectsAccelOutOfRange(double accelZ)
    {
        PositionPayloadDto payload = ValidPayload() with { AccelZG = accelZ };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.AccelOutOfRange, reason);
    }

    [Fact]
    public void CarriesTemperatureWhenPresent()
    {
        PositionPayloadDto payload = ValidPayload() with { TempC = 42.5 };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? position, out PositionRejectReason _);

        Assert.True(valid);
        Assert.NotNull(position);
        Assert.Equal(42.5, position.TemperatureC);
    }

    [Theory]
    [InlineData(-40.1)]
    [InlineData(125.1)]
    [InlineData(double.NaN)]
    public void RejectsTemperatureOutOfRange(double temperatureC)
    {
        PositionPayloadDto payload = ValidPayload() with { TempC = temperatureC };

        bool valid = CreateValidator().TryValidate(
            payload, TopicDeviceId, s_utcNow, out ValidatedPosition? _, out PositionRejectReason reason);

        Assert.False(valid);
        Assert.Equal(PositionRejectReason.TemperatureOutOfRange, reason);
    }
}
