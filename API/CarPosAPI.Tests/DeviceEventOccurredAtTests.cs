using CarPosAPI.Services.Ingest;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins down when a stored event says it happened. Live messages keep the server's
/// receive time - the clock every other timestamp shares - and only a message that
/// plainly waited on the device's SD card is placed by the device's own clock.
/// </summary>
public sealed class DeviceEventOccurredAtTests
{
    private static readonly DateTime s_receivedAt = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutADeviceClockTheArrivalIsTheTime()
    {
        Assert.Equal(s_receivedAt, DeviceStatusWriter.ResolveOccurredAt(s_receivedAt, null));
    }

    [Theory]
    // Live: seconds in flight, or a device clock that has drifted a little either way.
    [InlineData(-30)]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(45)]
    public void ALiveMessageKeepsTheReceiveTime(int deviceClockOffsetSeconds)
    {
        DateTime deviceTime = s_receivedAt.AddSeconds(-deviceClockOffsetSeconds);

        Assert.Equal(s_receivedAt, DeviceStatusWriter.ResolveOccurredAt(s_receivedAt, deviceTime));
    }

    [Fact]
    public void AMessageThatWaitedOnTheCardKeepsTheDeviceTime()
    {
        // Parked out of range at 08:15, delivered when the car came home at noon.
        DateTime deviceTime = new DateTime(2026, 10, 9, 8, 15, 0, DateTimeKind.Utc);

        Assert.Equal(deviceTime, DeviceStatusWriter.ResolveOccurredAt(s_receivedAt, deviceTime));
    }

    [Fact]
    public void TheThresholdItselfStillCountsAsLive()
    {
        DateTime deviceTime = s_receivedAt - DeviceStatusWriter.ReplayThreshold;

        Assert.Equal(s_receivedAt, DeviceStatusWriter.ResolveOccurredAt(s_receivedAt, deviceTime));
    }

    [Fact]
    public void AFastDeviceClockNeverMovesAnEventIntoTheFuture()
    {
        DateTime deviceTime = s_receivedAt.AddMinutes(10);

        Assert.Equal(s_receivedAt, DeviceStatusWriter.ResolveOccurredAt(s_receivedAt, deviceTime));
    }
}
