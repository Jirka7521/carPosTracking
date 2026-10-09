using CarPosAPI.Services.Ingest;

namespace CarPosAPI.Tests;

/// <summary>
/// The routing decision in front of both ingest pipelines: only exactly
/// <c>devices/&lt;id&gt;/status</c> with a well-formed id is a status message.
/// Everything else falls through to the position pipeline, which has its own guard.
/// </summary>
public sealed class DeviceStatusTopicTests
{
    [Theory]
    [InlineData("devices/GNSS01/status", "GNSS01")]
    [InlineData("devices/a-b_9/status", "a-b_9")]
    public void ParsesAStatusTopic(string topic, string expected)
    {
        bool parsed = DeviceStatusTopic.TryParseDeviceId(topic, out string? deviceId);

        Assert.True(parsed);
        Assert.Equal(expected, deviceId);
    }

    [Theory]
    // Telemetry and the API's own topics are not status topics.
    [InlineData("devices/GNSS01")]
    [InlineData("devices/GNSS01/ack")]
    [InlineData("devices/GNSS01/config")]
    // Nothing between prefix and suffix, or more than one segment.
    [InlineData("devices/status")]
    [InlineData("devices//status")]
    [InlineData("devices/a/b/status")]
    // Wildcards and other characters a real device id cannot contain.
    [InlineData("devices/+/status")]
    [InlineData("devices/#/status")]
    [InlineData("devices/GNSS 01/status")]
    // Case-sensitive, like MQTT itself.
    [InlineData("Devices/GNSS01/status")]
    [InlineData("devices/GNSS01/Status")]
    public void RejectsAnythingElse(string topic)
    {
        bool parsed = DeviceStatusTopic.TryParseDeviceId(topic, out string? deviceId);

        Assert.False(parsed);
        Assert.Null(deviceId);
    }
}
