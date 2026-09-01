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
/// then talks to the wrong topic.
/// </para>
///
/// <para>
/// <b>The template is a verbatim copy of the firmware's own
/// <c>Config.example.h</c></b>, staged into <c>ConfigTemplate.h.txt</c> by an
/// MSBuild target and embedded — it cannot be read from <c>ESP32/</c> at runtime
/// because <c>Container/App/docker-compose.yml</c> builds this image with
/// <c>../../API</c> as its context, so the firmware tree does not exist where this
/// code runs. Editing <c>Config.example.h</c> is therefore the only edit needed to
/// change what the dashboard hands out.
/// </para>
///
/// <para>
/// That copy used to be hand-maintained, with <c>{{TOKEN}}</c> holes cut into it,
/// and it drifted: the firmware gained three fix-averaging constants, the copy did
/// not, and the dashboard served a file that no longer compiled. So this class no
/// longer substitutes into holes — it rewrites constants <b>by name</b> through
/// <see cref="ConfigConstantWriter"/>, which means a constant nobody names here
/// passes straight through with its example value and a new firmware constant needs
/// no edit at all. <c>ConfigSnippetBuilderTests.StagedTemplateMatchesFirmwareSource</c>
/// fails the build if the staged copy and the firmware source ever disagree.
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

        ConfigConstantWriter writer = new ConfigConstantWriter(s_template);

        // The firmware file introduces itself as the committed example and tells the
        // reader to copy it to Config.h and fill in the WiFi credentials. This IS their
        // Config.h, so that paragraph is an instruction to redo work already done.
        writer.RemoveHeaderBlockMentioning("Config.example.h");

        // --- This device's identity, topics and broker -------------------------
        writer.SetString("kDeviceId", deviceId);
        writer.SetString("kTelemetryTopic", TelemetryTopicFor(deviceId));
        writer.SetString("kConfigTopic", ConfigTopicFor(deviceId));
        writer.SetString("kAckTopic", AckTopicFor(deviceId));
        writer.SetString("kMqttBrokerUri", brokerUri);

        // The broker account is per device and named after it, and the client id is
        // what the operator sees in Mosquitto's log — both are the device id, and the
        // firmware template ships "admin"/"GNSSXX" placeholders for them.
        writer.SetString("kMqttUsername", deviceId);
        writer.SetString("kMqttClientId", deviceId);

        // --- Secrets: forced blank, never filled ------------------------------
        // The firmware template already leaves these empty, but stating it here means
        // a value accidentally committed to Config.example.h could still never travel
        // out through this endpoint. See the class summary for why each one is not
        // the server's to know. The dashboard fills them in the operator's browser.
        writer.SetString("kWifiSsid", string.Empty);
        writer.SetString("kWifiPassword", string.Empty);
        writer.SetString("kMqttPassword", string.Empty);
        writer.SetString("kDeviceAckPrivateKeyPem", string.Empty);

        // Without credentials the station would fail to associate on every wake and
        // burn the power budget doing it. The browser flips this back on the moment
        // an SSID is typed; the firmware template ships it true.
        writer.SetBool("kWifiEnabled", false);

        // --- Delivery acknowledgements ----------------------------------------
        writer.InsertCommentAbove("kAckEnabled", BuildAckNote(deviceId, ackPublicKeyFingerprint));

        // Acks are only meaningful once the API holds a key to seal them with.
        // Rendering `true` regardless — as this used to — produces firmware that waits
        // out the ack timeout on every single fix, with nothing in the logs to say why.
        writer.SetBool("kAckEnabled", ackPublicKeyFingerprint is not null);

        // --- Receiver public key ----------------------------------------------
        writer.InsertCommentAbove("kReceiverPublicKeyPem", BuildKeyFingerprintNote(publicKeyFingerprint));
        writer.SetMultiLineLiteral("kReceiverPublicKeyPem", BuildPemLiteral(publicKeyPem));

        // --- Defaults: what this device is running now -------------------------
        writer.SetNumber("kDefaultSendIntervalSeconds", Number(settings.IntervalSeconds));
        writer.SetBool("kDefaultSleepBetweenSends", settings.SleepBetween);
        writer.SetNumber("kFixAcquireTimeoutSeconds", Number(settings.FixTimeoutSeconds));
        writer.SetNumber("kSdMaxQueuedFixes", Number(settings.QueueMaxFixes));
        writer.SetNumber("kRetryIntervalHours", Number(settings.RetryIntervalHours));
        writer.SetNumber("kRetryMaxAgeHours", Number(settings.RetryMaxAgeHours));
        writer.SetNumber("kDefaultConfigCheckSeconds", Number(settings.ConfigCheckSeconds));

        // --- Bounds: rendered from the API's own rules -------------------------
        // This removes the hand-sync the firmware's comment asks for ("if you change
        // one here, change it there too") for the file the dashboard hands out.
        writer.SetNumber("kMinSendIntervalSeconds", Number(DeviceConfigRules.MinIntervalSeconds));
        writer.SetNumber("kMaxSendIntervalSeconds", Number(DeviceConfigRules.MaxIntervalSeconds));
        writer.SetNumber("kMinFixTimeoutSeconds", Number(DeviceConfigRules.MinFixTimeoutSeconds));
        writer.SetNumber("kMaxFixTimeoutSeconds", Number(DeviceConfigRules.MaxFixTimeoutSeconds));
        writer.SetNumber("kMinQueueMaxFixes", Number(DeviceConfigRules.MinQueueMaxFixes));
        writer.SetNumber("kMaxQueueMaxFixes", Number(DeviceConfigRules.MaxQueueMaxFixes));
        writer.SetNumber("kMinRetryIntervalHours", Number(DeviceConfigRules.MinRetryIntervalHours));
        writer.SetNumber("kMaxRetryIntervalHours", Number(DeviceConfigRules.MaxRetryIntervalHours));
        writer.SetNumber("kMaxRetryMaxAgeHours", Number(DeviceConfigRules.MaxRetryMaxAgeHours));
        writer.SetNumber("kMinConfigCheckSeconds", Number(DeviceConfigRules.MinConfigCheckSeconds));
        writer.SetNumber("kMaxConfigCheckSeconds", Number(DeviceConfigRules.MaxConfigCheckSeconds));

        // Last, so it sits above the template's own header rather than inside it: the
        // firmware file opens by explaining it is the committed example, which is no
        // longer true of what the operator is holding.
        writer.PrependBanner(BuildBanner(deviceId, generatedAtUtc));

        return writer.ToString();
    }

    /// <summary>
    /// Builds the header that turns the firmware's committed example into "this
    /// device's file": what it is, when it was rendered, and — the part operators
    /// actually need — which four values are still blank and why the server could not
    /// have filled them in. Comments are legal before <c>#pragma once</c>, so this
    /// prepends rather than replacing the template's own banner.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="generatedAtUtc">Render time (UTC).</param>
    /// <returns>The banner, <c>\n</c>-separated, with no trailing newline.</returns>
    private static string BuildBanner(string deviceId, DateTime generatedAtUtc)
    {
        string timestamp = generatedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        StringBuilder banner = new StringBuilder();

        banner.Append("// =============================================================================\n");
        banner.Append(CultureInfo.InvariantCulture, $"//  Config.h  -  generated for carPosTracking device \"{deviceId}\"\n");
        banner.Append(CultureInfo.InvariantCulture, $"//               rendered {timestamp} by the dashboard.\n");
        banner.Append("// -----------------------------------------------------------------------------\n");
        banner.Append("//  This is a COMPLETE, ready-to-build config file. Save it as\n");
        banner.Append("//      ESP32/src/config/Config.h\n");
        banner.Append("//  and run `pio run`. Nothing else needs merging in.\n");
        banner.Append("//\n");
        banner.Append("//  Everything specific to this device - its id, MQTT topics, broker URI and the\n");
        banner.Append("//  receiver public key it encrypts to - is already filled in below.\n");
        banner.Append("//\n");
        banner.Append("//  WHAT IS STILL BLANK (and why):\n");
        banner.Append("//      kWifiSsid / kWifiPassword     your network. Set them and flip\n");
        banner.Append("//                                    kWifiEnabled to true.\n");
        banner.Append("//      kMqttPassword                 the broker account is created by hand on\n");
        banner.Append("//                                    the server with mosquitto_passwd - the API\n");
        banner.Append("//                                    does not issue MQTT credentials, so it\n");
        banner.Append("//                                    cannot fill in one it never minted.\n");
        banner.Append("//      kDeviceAckPrivateKeyPem       this device's own private key. It must\n");
        banner.Append("//                                    never reach the server, so the dashboard\n");
        banner.Append("//                                    generates it in your browser and weaves it\n");
        banner.Append("//                                    in there - it was never sent to the API.\n");
        banner.Append("//\n");
        banner.Append("//  Config.h is git-ignored precisely because of those four values. Never commit\n");
        banner.Append("//  it, and never paste them into Config.example.h.\n");
        banner.Append("// =============================================================================");

        return banner.ToString();
    }

    /// <summary>
    /// Builds the comment naming the receiver key this file was rendered against. The
    /// fingerprint is the only way to tell, later, whether a tracker in the field is
    /// encrypting to the key the API still holds — the PEM below it is 800 characters
    /// of base64 that nobody compares by eye.
    /// </summary>
    /// <param name="publicKeyFingerprint">SPKI-SHA256 hex of the receiver public key.</param>
    /// <returns>Comment lines, <c>\n</c>-separated, with no trailing newline.</returns>
    private static string BuildKeyFingerprintNote(string publicKeyFingerprint)
    {
        StringBuilder note = new StringBuilder();

        note.Append("// The receiver PUBLIC key below is this device's own: its matching private half\n");
        note.Append("// was generated when the device was registered, is encrypted at rest under the\n");
        note.Append("// API's master key, and has no code path out of the database - which is what\n");
        note.Append("// stops the broker (and anyone who steals this tracker or its SD card) from\n");
        note.Append("// reading positions.\n");
        note.Append("//\n");
        note.Append("// Fingerprint (SHA-256 over the DER SubjectPublicKeyInfo):\n");
        note.Append(CultureInfo.InvariantCulture, $"//     {publicKeyFingerprint}");

        return note.ToString();
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
