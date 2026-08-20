using System.Globalization;
using System.Reflection;
using System.Text;
using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// Renders the firmware-facing view of a provisioned device: the MQTT topics it
/// owns, and a <b>complete <c>Config.h</c></b> — this device's identity, topics,
/// broker URI, receiver public key and current setting defaults dropped into the
/// firmware's own template — ready to be saved over
/// <c>ESP32/src/config/Config.h</c> and built.
///
/// <para>
/// It renders a whole file rather than a block of lines because a block still had
/// to be merged by hand into a copy of <c>Config.example.h</c>, which is exactly
/// the step that goes wrong: a missed constant produces firmware that compiles and
/// then talks to the wrong topic. The file comes from
/// <c>ConfigTemplate.h.txt</c>, an embedded copy of the firmware's
/// <c>Config.example.h</c> with <c>{{TOKEN}}</c> holes cut in it.
/// </para>
///
/// <para>
/// <b>That template is a copy, and copies drift.</b> It cannot be read from
/// <c>ESP32/</c> at runtime: <c>Container/App/docker-compose.yml</c> builds this
/// image with <c>../../API</c> as its context, so the firmware tree does not exist
/// where this code runs. The guard is
/// <c>ConfigSnippetBuilderTests.RendersACompleteCompilableFile</c>, which fails the
/// build when the firmware gains a constant this template does not have — keep it
/// honest rather than deleting it.
/// </para>
///
/// <para>
/// This class is also the one place that knows the firmware's constant names, so a
/// rename on the device side is a single-file change here. It is why the output is
/// worth unit-testing: a mistake in the PEM-to-C-string-literal conversion produces
/// a file that fails to compile only once it has been pasted into the firmware, far
/// away from this code.
/// </para>
///
/// <para>
/// <b>Deliberately left empty:</b> <c>kMqttPassword</c> (the API does not manage
/// broker accounts — those are created by hand on the server), <c>kWifiSsid</c> /
/// <c>kWifiPassword</c> (not the server's to know), and
/// <c>kDeviceAckPrivateKeyPem</c> (the device's own secret, which must never reach
/// the server at all). The dashboard fills those four in inside the operator's
/// browser, so none of them travels through this API.
/// </para>
/// </summary>
internal sealed class ConfigSnippetBuilder
{
    /// <summary>Topic prefix all device telemetry is published under.</summary>
    private const string TopicPrefix = "devices/";

    /// <summary>Suffix of the retained per-device settings topic.</summary>
    private const string ConfigTopicSuffix = "/config";

    /// <summary>Suffix of the per-device delivery-ack topic.</summary>
    private const string AckTopicSuffix = "/ack";

    /// <summary>Indent of a continued C string literal, matching the firmware's style.</summary>
    private const string LiteralIndent = "    ";

    /// <summary>
    /// Manifest name of the embedded template: root namespace plus folder path plus
    /// file name. Pinned as a constant (and to an explicit <c>LogicalName</c> in the
    /// csproj) so moving the file produces a loud failure here rather than a silent
    /// one at first request.
    /// </summary>
    private const string TemplateResourceName = "CarPosAPI.Services.Provisioning.ConfigTemplate.h.txt";

    /// <summary>
    /// The template, read once. It is immutable and a few tens of KB, so re-reading
    /// the resource stream per provisioning call would be pure waste.
    /// </summary>
    private static readonly string s_template = LoadTemplate();

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

    /// <summary>Builds the delivery-ack topic for a device.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <returns>The topic the API confirms deliveries on, e.g. <c>devices/GNSS01/ack</c>.</returns>
    public string AckTopicFor(string deviceId)
    {
        return TopicPrefix + deviceId + AckTopicSuffix;
    }

    /// <summary>Renders the complete, ready-to-build <c>Config.h</c>.</summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="brokerUri">Broker URI the API is configured against.</param>
    /// <param name="publicKeyPem">Receiver RSA-3072 public key in SPKI PEM form.</param>
    /// <param name="publicKeyFingerprint">SPKI-SHA256 hex, quoted in a comment for traceability.</param>
    /// <param name="ackPublicKeyFingerprint">
    /// SPKI-SHA256 of the device's imported ack public key, or null when none has
    /// been imported. Only the fingerprint: the ack <em>private</em> key belongs to
    /// the device alone, precisely so that no device secret can ever travel in this
    /// file — which is re-readable through the provisioning endpoint and lands on
    /// the operator's clipboard.
    /// </param>
    /// <param name="settings">
    /// The device's current remote settings, rendered as the compile-time defaults.
    /// Using the live values rather than the factory ones means a freshly flashed
    /// tracker already behaves correctly on its very first cycle, before the broker
    /// has replayed the retained config document to it.
    /// </param>
    /// <param name="generatedAtUtc">Render time (UTC), stamped into the header comment.</param>
    /// <returns>The whole file, newline-separated with <c>\n</c>.</returns>
    public string Build(
        string deviceId,
        string brokerUri,
        string publicKeyPem,
        string publicKeyFingerprint,
        string? ackPublicKeyFingerprint,
        DeviceConfigValuesDto settings,
        DateTime generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string timestamp = generatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // One dictionary rather than a chain of Replace calls so the token list reads
        // as a table against the template, and so a token added to one side without
        // the other shows up as an obviously missing row.
        Dictionary<string, string> tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GENERATED_AT"] = timestamp,
            ["DEVICE_ID"] = deviceId,
            ["TELEMETRY_TOPIC"] = TelemetryTopicFor(deviceId),
            ["CONFIG_TOPIC"] = ConfigTopicFor(deviceId),
            ["ACK_TOPIC"] = AckTopicFor(deviceId),
            ["BROKER_URI"] = brokerUri,
            ["PUBLIC_KEY_FINGERPRINT"] = publicKeyFingerprint,
            ["RECEIVER_PUBLIC_KEY_LITERAL"] = BuildPemLiteral(publicKeyPem),
            ["ACK_BLOCK_NOTE"] = BuildAckNote(deviceId, ackPublicKeyFingerprint),

            // Acks are only meaningful once the API holds a key to seal them with.
            // Rendering `true` regardless — as this used to — produces firmware that
            // waits out the ack timeout on every single fix, with nothing in the logs
            // to say why.
            ["ACK_ENABLED"] = ackPublicKeyFingerprint is null ? "false" : "true",

            // Defaults: what this device is running now.
            ["DEFAULT_INTERVAL_S"] = Number(settings.IntervalSeconds),
            ["DEFAULT_SLEEP_BETWEEN"] = settings.SleepBetween ? "true" : "false",
            ["DEFAULT_FIX_TIMEOUT_S"] = Number(settings.FixTimeoutSeconds),
            ["DEFAULT_QUEUE_MAX_FIXES"] = Number(settings.QueueMaxFixes),
            ["DEFAULT_RETRY_INTERVAL_H"] = Number(settings.RetryIntervalHours),
            ["DEFAULT_RETRY_MAX_AGE_H"] = Number(settings.RetryMaxAgeHours),
            ["DEFAULT_CONFIG_CHECK_S"] = Number(settings.ConfigCheckSeconds),

            // Bounds: rendered from the API's own rules, which removes the hand-sync
            // the firmware's comment used to ask for ("if you change one here, change
            // it there too").
            ["MIN_INTERVAL_S"] = Number(DeviceConfigRules.MinIntervalSeconds),
            ["MAX_INTERVAL_S"] = Number(DeviceConfigRules.MaxIntervalSeconds),
            ["MIN_FIX_TIMEOUT_S"] = Number(DeviceConfigRules.MinFixTimeoutSeconds),
            ["MAX_FIX_TIMEOUT_S"] = Number(DeviceConfigRules.MaxFixTimeoutSeconds),
            ["MIN_QUEUE_MAX_FIXES"] = Number(DeviceConfigRules.MinQueueMaxFixes),
            ["MAX_QUEUE_MAX_FIXES"] = Number(DeviceConfigRules.MaxQueueMaxFixes),
            ["MIN_RETRY_INTERVAL_H"] = Number(DeviceConfigRules.MinRetryIntervalHours),
            ["MAX_RETRY_INTERVAL_H"] = Number(DeviceConfigRules.MaxRetryIntervalHours),
            ["MAX_RETRY_MAX_AGE_H"] = Number(DeviceConfigRules.MaxRetryMaxAgeHours),
            ["MIN_CONFIG_CHECK_S"] = Number(DeviceConfigRules.MinConfigCheckSeconds),
            ["MAX_CONFIG_CHECK_S"] = Number(DeviceConfigRules.MaxConfigCheckSeconds),
        };

        StringBuilder rendered = new StringBuilder(s_template);
        foreach (KeyValuePair<string, string> token in tokens)
        {
            rendered.Replace("{{" + token.Key + "}}", token.Value);
        }

        return rendered.ToString();
    }

    /// <summary>Formats an integer for a C++ literal, culture-independently.</summary>
    /// <param name="value">The value to render.</param>
    /// <returns>Its invariant decimal form.</returns>
    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the comment block above <c>kAckEnabled</c>. It carries no key material
    /// by design — only the fingerprint of what the server holds, plus the commands
    /// for minting the pair by hand. That asymmetry is the point: for telemetry the
    /// server keeps the private half, but an ack is sealed <em>to</em> the device, so
    /// the device's private key must never exist on the server or in this file.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ackPublicKeyFingerprint">Fingerprint on file, or null when unimported.</param>
    /// <returns>Comment lines, <c>\n</c>-separated, with no trailing newline.</returns>
    private static string BuildAckNote(string deviceId, string? ackPublicKeyFingerprint)
    {
        StringBuilder note = new StringBuilder();

        if (ackPublicKeyFingerprint is null)
        {
            note.Append("// NOT YET CONFIGURED: no ack public key has been imported for this device,\n");
            note.Append("// so the API sends it no acks and kAckEnabled is rendered false below. The\n");
            note.Append("// device holds the private half of this pair, so it must be generated off the\n");
            note.Append("// server: use the dashboard's \"generate a new ack key pair\" button, which does\n");
            note.Append("// it in your browser, or do it by hand -\n");
        }
        else
        {
            note.Append(CultureInfo.InvariantCulture, $"// Ack public key on file: SPKI-SHA256 {ackPublicKeyFingerprint}.\n");
            note.Append("// Its matching private half belongs in this file only (kDeviceAckPrivateKeyPem\n");
            note.Append("// below). To rotate it, use the dashboard's \"generate a new ack key pair\"\n");
            note.Append("// button, or do it by hand -\n");
        }

        // Each command on ONE line. A trailing backslash would be the natural way to
        // wrap these, and it is exactly wrong here: a '\' at the end of a // line
        // continues the comment onto the next one, and the firmware builds with
        // -Werror=comment, so the file the operator pasted would not compile.
        note.Append("//\n");
        note.Append(CultureInfo.InvariantCulture, $"//   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out {deviceId}_ack_private.pem\n");
        note.Append(CultureInfo.InvariantCulture, $"//   openssl rsa -in {deviceId}_ack_private.pem -pubout -out {deviceId}_ack_public.pem\n");
        note.Append(CultureInfo.InvariantCulture, $"//   dotnet run -- import-device-key --device {deviceId} --ack-public-pem {deviceId}_ack_public.pem\n");
        note.Append("//\n");
        note.Append("// - then paste the PRIVATE half into kDeviceAckPrivateKeyPem below, set\n");
        note.Append("// kAckEnabled to true, and delete the loose .pem once it is in.");

        return note.ToString();
    }

    /// <summary>
    /// Renders a PEM as the firmware's multi-line C string literal: one quoted,
    /// <c>\n</c>-terminated line per PEM line, the last one carrying the semicolon.
    /// </summary>
    /// <param name="publicKeyPem">The PEM to convert.</param>
    /// <returns>The literal, <c>\n</c>-separated, with no trailing newline.</returns>
    private static string BuildPemLiteral(string publicKeyPem)
    {
        // TrimEntries copes with the CR of a CRLF PEM; RemoveEmptyEntries drops the
        // trailing blank left by the final newline.
        string[] lines = publicKeyPem.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        StringBuilder literal = new StringBuilder();

        for (int index = 0; index < lines.Length; index++)
        {
            // No escaping pass is needed: a PEM body is base64 and its delimiters
            // are dashes and spaces, so it can contain neither a quote nor a
            // backslash — the only two characters that would need one here.
            literal.Append(LiteralIndent);
            literal.Append('"');
            literal.Append(lines[index]);
            literal.Append("\\n\"");
            literal.Append(index == lines.Length - 1 ? ";" : "\n");
        }

        return literal.ToString();
    }

    /// <summary>Reads the embedded template and normalises its line endings.</summary>
    /// <returns>The template text with <c>\n</c> endings.</returns>
    /// <exception cref="InvalidOperationException">The resource is not embedded in the assembly.</exception>
    private static string LoadTemplate()
    {
        Assembly assembly = typeof(ConfigSnippetBuilder).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(TemplateResourceName);

        if (stream is null)
        {
            // A packaging error, not a request error: the file is compiled into the
            // assembly, so if it is missing here it is missing for every caller.
            throw new InvalidOperationException(
                $"The firmware config template '{TemplateResourceName}' is not embedded in the assembly. "
                + "Check the <EmbeddedResource> item in CarPosAPI.csproj.");
        }

        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);

        // The file travels through JSON into a Git-tracked C++ file, so it must carry
        // LF regardless of how the template happened to be checked out on the build
        // machine — a CRLF inside a continued C string literal is a compile error.
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
