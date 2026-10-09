using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Recognises a device status topic, <c>devices/&lt;id&gt;/status</c>, and pulls the
/// device id out of it — the routing decision <see cref="MqttIngestService"/> makes for
/// every message before choosing a pipeline.
///
/// The same strictness as <see cref="IngestPipeline"/>'s telemetry parser, and the same
/// id shape: a status message must name a device exactly the way its fixes do, or the
/// device registry would resolve the two to different rows.
/// </summary>
internal static partial class DeviceStatusTopic
{
    /// <summary>Topic prefix every device topic sits under.</summary>
    private const string TopicPrefix = "devices/";

    /// <summary>The last segment that makes a device topic a status topic.</summary>
    public const string TopicSuffix = "/status";

    /// <summary>Extracts and validates the device id from a status topic.</summary>
    /// <param name="topic">The raw MQTT topic.</param>
    /// <param name="deviceId">The device id when the method returns true.</param>
    /// <returns>True only for exactly <c>devices/&lt;id&gt;/status</c> with a well-formed id.</returns>
    public static bool TryParseDeviceId(string topic, [NotNullWhen(true)] out string? deviceId)
    {
        deviceId = null;

        if (!topic.StartsWith(TopicPrefix, StringComparison.Ordinal)
            || !topic.EndsWith(TopicSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        // Guarded explicitly rather than by slicing: "devices/status" would otherwise
        // overlap prefix and suffix and produce a negative-length range.
        int idLength = topic.Length - TopicPrefix.Length - TopicSuffix.Length;
        if (idLength <= 0)
        {
            return false;
        }

        string candidate = topic.Substring(TopicPrefix.Length, idLength);

        // One segment of safe characters — the regex excludes '/' and the MQTT
        // wildcards, so devices/a/b/status cannot slip through as id "a/b".
        if (!DeviceIdRegex().IsMatch(candidate))
        {
            return false;
        }

        deviceId = candidate;
        return true;
    }

    /// <summary>Allowed device-id shape — identical to the telemetry topic's.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex DeviceIdRegex();
}
