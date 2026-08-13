using System.Globalization;
using System.Text;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// Renders the firmware-facing view of a provisioned device: the MQTT topics it
/// owns, and a block of C++ <c>constexpr</c> lines ready to paste over the
/// matching constants in <c>ESP32/src/config/Config.h</c>.
///
/// This class is the one place that knows the firmware's constant names, so a
/// rename on the device side is a single-file change here. It is also why the
/// output is worth unit-testing: a mistake in the PEM-to-C-string-literal
/// conversion produces a snippet that fails to compile only once it has been
/// pasted into the firmware, far away from this code.
///
/// Deliberately not included: <c>kMqttPassword</c> is emitted empty. The API does
/// not manage broker accounts — those are created by hand on the server — so the
/// snippet must not pretend to know a credential it never issued.
/// </summary>
internal sealed class ConfigSnippetBuilder
{
    /// <summary>Topic prefix all device telemetry is published under.</summary>
    private const string TopicPrefix = "devices/";

    /// <summary>Suffix of the retained per-device settings topic.</summary>
    private const string ConfigTopicSuffix = "/config";

    /// <summary>Indent of a continued C string literal, matching the firmware's style.</summary>
    private const string LiteralIndent = "    ";

    /// <summary>Builds the telemetry topic for a device.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <returns>The topic the firmware publishes fixes to, e.g. <c>devices/GNSS01</c>.</returns>
    public string TelemetryTopicFor(string deviceId)
    {
        return TopicPrefix + deviceId;
    }

    /// <summary>Builds the retained-settings topic for a device.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <returns>The topic the firmware reads its config from, e.g. <c>devices/GNSS01/config</c>.</returns>
    public string ConfigTopicFor(string deviceId)
    {
        return TopicPrefix + deviceId + ConfigTopicSuffix;
    }

    /// <summary>Renders the paste-ready <c>Config.h</c> block.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="brokerUri">Broker URI the API is configured against.</param>
    /// <param name="publicKeyPem">Receiver RSA-3072 public key in SPKI PEM form.</param>
    /// <param name="publicKeyFingerprint">SPKI-SHA256 hex, quoted in a comment for traceability.</param>
    /// <param name="generatedAtUtc">Provisioning time (UTC), stamped into the header comment.</param>
    /// <returns>The snippet, newline-separated with <c>\n</c>.</returns>
    public string Build(
        string deviceId,
        string brokerUri,
        string publicKeyPem,
        string publicKeyFingerprint,
        DateTime generatedAtUtc)
    {
        // '\n' rather than Environment.NewLine: the output travels through JSON to
        // an editor and into a Git-tracked C++ file, none of which want CRLF just
        // because the API happens to run on Windows. It also keeps tests stable.
        StringBuilder builder = new StringBuilder();

        string timestamp = generatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        builder.Append("// ---------------------------------------------------------------------------\n");
        builder.Append(CultureInfo.InvariantCulture, $"//  carPosTracking device \"{deviceId}\" — provisioned {timestamp}\n");
        builder.Append("//  Paste over the matching constants in ESP32/src/config/Config.h.\n");
        builder.Append("// ---------------------------------------------------------------------------\n");
        builder.Append(CultureInfo.InvariantCulture, $"constexpr char kDeviceId[]       = \"{deviceId}\";\n");
        builder.Append(CultureInfo.InvariantCulture, $"constexpr char kMqttClientId[]   = \"{deviceId}\";\n");
        builder.Append(CultureInfo.InvariantCulture, $"constexpr char kTelemetryTopic[] = \"{TelemetryTopicFor(deviceId)}\";\n");
        builder.Append(CultureInfo.InvariantCulture, $"constexpr char kConfigTopic[]    = \"{ConfigTopicFor(deviceId)}\";\n");
        builder.Append(CultureInfo.InvariantCulture, $"constexpr char kMqttBrokerUri[]  = \"{brokerUri}\";\n");
        builder.Append('\n');
        builder.Append("// The broker account is created by hand on the server — the API does not\n");
        builder.Append("// manage MQTT credentials. Fill the password in from your mosquitto_passwd\n");
        builder.Append("// entry, and keep it in Config.h only (never in Config.example.h).\n");
        builder.Append(CultureInfo.InvariantCulture, $"constexpr char kMqttUsername[] = \"{deviceId}\";\n");
        builder.Append("constexpr char kMqttPassword[] = \"\";  // <-- set this yourself\n");
        builder.Append('\n');
        builder.Append(CultureInfo.InvariantCulture, $"// Receiver RSA-3072 public key (SPKI-SHA256 {publicKeyFingerprint}).\n");
        builder.Append("// The matching private key stays encrypted in the API database and never\n");
        builder.Append("// leaves the server, so this device cannot read back its own positions.\n");
        builder.Append("constexpr char kReceiverPublicKeyPem[] =\n");
        AppendPemLiteral(builder, publicKeyPem);

        return builder.ToString();
    }

    /// <summary>
    /// Emits a PEM as the firmware's multi-line C string literal: one quoted,
    /// <c>\n</c>-terminated line per PEM line, the last one carrying the semicolon.
    /// </summary>
    /// <param name="builder">Target buffer.</param>
    /// <param name="publicKeyPem">The PEM to convert.</param>
    private static void AppendPemLiteral(StringBuilder builder, string publicKeyPem)
    {
        // TrimEntries copes with the CR of a CRLF PEM; RemoveEmptyEntries drops the
        // trailing blank left by the final newline.
        string[] lines = publicKeyPem.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (int index = 0; index < lines.Length; index++)
        {
            // No escaping pass is needed: a PEM body is base64 and its delimiters
            // are dashes and spaces, so it can contain neither a quote nor a
            // backslash — the only two characters that would need one here.
            builder.Append(LiteralIndent);
            builder.Append('"');
            builder.Append(lines[index]);
            builder.Append("\\n\"");
            builder.Append(index == lines.Length - 1 ? ";\n" : "\n");
        }
    }
}
