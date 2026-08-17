using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Provisioning;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the <c>Config.h</c> the provisioning endpoint hands out. The stakes are
/// higher than they look: it is now a <em>complete file</em> that gets saved straight
/// over the firmware's config and built, so a missing constant or a mangled string
/// literal surfaces as a compile error a long way from this code — or, worse, as a
/// device that builds fine and talks to the wrong topic.
///
/// These tests assert the exact literal shape the firmware expects, that every
/// constant the firmware references is present, and that no secret is ever filled in:
/// not the broker password (the API does not issue MQTT credentials) and above all not
/// the device's ack private key (which must never reach this server at all).
/// </summary>
public sealed class ConfigSnippetBuilderTests
{
    private const string DeviceId = "GNSS01";

    private const string BrokerUri = "wss://jimajer.cz:443/mqttBroker";

    private const string Fingerprint = "ABCDEF0123456789";

    private const string AckFingerprint = "0123456789ABCDEF";

    private static readonly DateTime s_generatedAt = new DateTime(2026, 7, 22, 10, 15, 0, DateTimeKind.Utc);

    /// <summary>
    /// Settings deliberately different from every factory default, so a test asserting
    /// that a rendered default came from the device's own configuration cannot pass by
    /// accidentally matching <see cref="DeviceConfigRules"/>.
    /// </summary>
    private static readonly DeviceConfigValuesDto s_settings = new DeviceConfigValuesDto(
        IntervalSeconds: 300,
        SleepBetween: true,
        FixTimeoutSeconds: 240,
        QueueMaxFixes: 5000,
        RetryIntervalHours: 12,
        RetryMaxAgeHours: 72,
        ConfigCheckSeconds: 1800);

    /// <summary>
    /// Every <c>config::k*</c> constant the firmware's <c>src/</c> tree references,
    /// plus the two reserved ADXL interrupt pins it declares. This list is the drift
    /// guard: when the firmware gains a constant and the embedded template does not,
    /// <see cref="RendersACompleteCompilableFile"/> is what says so — the alternative
    /// is finding out from a build error after the file has been pasted in.
    /// </summary>
    private static readonly string[] s_firmwareConstants =
    [
        // Modem / UART
        "kModemUartPort", "kModemTxPin", "kModemRxPin", "kModemBaudRate",
        "kModemBaudCandidates", "kModemBaudCandidateCount", "kModemPwrKeyPin",
        "kModemPwrKeyActiveLow",

        // GNSS
        "kEnableGps", "kEnableGlonass", "kEnableBeidou", "kEnableGalileo",
        "kGnssDebug", "kSatelliteScanMs", "kFixAcquireTimeoutSeconds", "kFixPollStepMs",

        // Accelerometer
        "kAdxlEnabled", "kI2cSdaPin", "kI2cSclPin", "kI2cClockHz", "kAdxlI2cAddress",
        "kAdxlInt1Pin", "kAdxlInt2Pin",

        // Battery
        "kBatteryEnabled", "kBatteryChargeSensePin", "kBatteryChargeAdcThreshold",
        "kBatteryEmptyMv", "kBatteryFullMv",

        // WiFi
        "kWifiEnabled", "kWifiSsid", "kWifiPassword", "kWifiConnectTimeoutMs",
        "kWifiMaxRetries", "kWifiReconnectIntervalMs",

        // MQTT + identity
        "kMqttEnabled", "kMqttBrokerUri", "kMqttUsername", "kMqttPassword",
        "kMqttClientId", "kMqttPublishAckTimeoutMs", "kDeviceId", "kTelemetryTopic",

        // Remote settings + acks
        "kConfigTopic", "kConfigFetchTimeoutMs", "kAckEnabled", "kAckTopic",
        "kAckTimeoutMs", "kDeviceAckPrivateKeyPem",
        "kDefaultSendIntervalSeconds", "kDefaultSleepBetweenSends",
        "kMinSendIntervalSeconds", "kMaxSendIntervalSeconds",
        "kMinFixTimeoutSeconds", "kMaxFixTimeoutSeconds",
        "kMinQueueMaxFixes", "kMaxQueueMaxFixes",
        "kMinRetryIntervalHours", "kMaxRetryIntervalHours", "kMaxRetryMaxAgeHours",
        "kDefaultConfigCheckSeconds", "kMinConfigCheckSeconds", "kMaxConfigCheckSeconds",

        // Crypto
        "kReceiverPublicKeyPem",

        // microSD
        "kSdEnabled", "kSdSpiHost", "kSdPinMiso", "kSdPinMosi", "kSdPinSclk", "kSdPinCs",
        "kSdMountPoint", "kSdQueueFilePath", "kSdSettingsFilePath", "kSdMaxQueuedFixes",
        "kSdMaxBurstFixes", "kBacklogFlushRetryMs", "kSdRetryFilePath",
        "kRetryIntervalHours", "kRetryMaxAgeHours", "kSdMaxRetryEntries",

        // Deep sleep
        "kWakeGpioPin", "kWakeGpioLevel", "kMinDeepSleepMs",
    ];

    /// <summary>Builds a file for a real generated key, as the service does.</summary>
    /// <param name="ackPublicKeyFingerprint">
    /// Ack key on file, or null for a device that has none yet — the state every
    /// device is in immediately after <c>POST /api/devices</c>.
    /// </param>
    /// <returns>The rendered file and the PEM it was built from.</returns>
    private static (string Snippet, string PublicKeyPem) BuildSnippet(
        string? ackPublicKeyFingerprint = null)
    {
        string publicKeyPem = TestKeys.ReceiverKey.ExportSubjectPublicKeyInfoPem();
        ConfigSnippetBuilder builder = new ConfigSnippetBuilder();
        string snippet = builder.Build(
            DeviceId,
            BrokerUri,
            publicKeyPem,
            Fingerprint,
            ackPublicKeyFingerprint,
            s_settings,
            s_generatedAt);
        return (snippet, publicKeyPem);
    }

    [Fact]
    public void DerivesTheFirmwareTopics()
    {
        ConfigSnippetBuilder builder = new ConfigSnippetBuilder();

        Assert.Equal("devices/GNSS01", builder.TelemetryTopicFor(DeviceId));
        Assert.Equal("devices/GNSS01/config", builder.ConfigTopicFor(DeviceId));
        Assert.Equal("devices/GNSS01/ack", builder.AckTopicFor(DeviceId));
    }

    [Fact]
    public void EmitsEveryConstantTheFirmwareNeeds()
    {
        (string snippet, string _) = BuildSnippet();

        Assert.Contains("constexpr char kDeviceId[]       = \"GNSS01\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kMqttClientId[] = \"GNSS01\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kTelemetryTopic[] = \"devices/GNSS01\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kConfigTopic[] = \"devices/GNSS01/config\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kAckTopic[] = \"devices/GNSS01/ack\";", snippet, StringComparison.Ordinal);
        Assert.Contains($"constexpr char kMqttBrokerUri[] = \"{BrokerUri}\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kReceiverPublicKeyPem[] =", snippet, StringComparison.Ordinal);
        Assert.Contains("rendered 2026-07-22T10:15:00Z", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersACompleteCompilableFile()
    {
        (string snippet, string _) = BuildSnippet();

        // The shell of a valid header, in order.
        Assert.Contains("#pragma once", snippet, StringComparison.Ordinal);
        Assert.Contains("namespace config {", snippet, StringComparison.Ordinal);
        Assert.EndsWith("}  // namespace config\n", snippet, StringComparison.Ordinal);

        // Every constant the firmware reads must actually be *declared*, not merely
        // mentioned in a comment — hence matching the declaration itself.
        foreach (string constant in s_firmwareConstants)
        {
            Assert.True(
                Regex.IsMatch(snippet, $@"^constexpr\s+\S+\s+{constant}\b", RegexOptions.Multiline),
                $"The rendered Config.h declares no '{constant}'. If the firmware gained it, add it to "
                    + "Services/Provisioning/ConfigTemplate.h.txt; if the firmware dropped it, remove it here.");
        }

        // A token the builder forgot to substitute would ship a literal "{{FOO}}" into
        // a C++ file — a compile error at best, a wrong topic at worst.
        Assert.DoesNotContain("{{", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesEverySecretEmpty()
    {
        (string snippet, string _) = BuildSnippet(AckFingerprint);

        // The API never issues MQTT credentials — the account is created by hand on
        // the server. Emitting anything here would be a lie at best.
        Assert.Contains("constexpr char kMqttPassword[] = \"\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kMqttUsername[] = \"GNSS01\";", snippet, StringComparison.Ordinal);

        // Not the server's to know: the dashboard fills these in the browser.
        Assert.Contains("constexpr char kWifiSsid[]     = \"\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kWifiPassword[] = \"\";", snippet, StringComparison.Ordinal);

        // And the one that matters most — see NeverLeaksPrivateKeyMaterial below.
        Assert.Contains("constexpr char kDeviceAckPrivateKeyPem[] = \"\";", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void DisablesWifiUntilCredentialsAreFilledIn()
    {
        (string snippet, string _) = BuildSnippet();

        // Enabling the station with no SSID would cost every boot — and every wake
        // from deep sleep — a full connect timeout waiting for an association that
        // cannot succeed. The dashboard flips this on when an SSID is typed in.
        Assert.Contains("constexpr bool kWifiEnabled = false;", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersEveryPemLineAsAQuotedCLiteral()
    {
        (string snippet, string publicKeyPem) = BuildSnippet();

        string[] pemLines = publicKeyPem.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string pemLine in pemLines)
        {
            // Each PEM line becomes:  ····"<line>\n"
            Assert.Contains($"    \"{pemLine}\\n\"", snippet, StringComparison.Ordinal);
        }

        Assert.Contains("\"-----BEGIN PUBLIC KEY-----\\n\"", snippet, StringComparison.Ordinal);
        // The final literal carries the statement's semicolon.
        Assert.Contains("\"-----END PUBLIC KEY-----\\n\";\n", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverLeaksPrivateKeyMaterial()
    {
        (string snippet, string _) = BuildSnippet();

        // The device only ever gets the receiver's public half; that private key stays
        // encrypted in the database. A regression here is a total compromise of the
        // end-to-end encryption, so it is asserted explicitly.
        //
        // Asserted on PEM *blocks* rather than the word "PRIVATE": the file's own prose
        // discusses the private keys at length, and a test that forbids the word would
        // be deleted the first time someone edited a comment.
        AssertNoPrivateKeyBlock(snippet);
    }

    [Fact]
    public void NeverLeaksPrivateKeyMaterialWhenAnAckKeyIsOnFile()
    {
        // The ack direction inverts the key roles — the *device* holds that private
        // key — which makes this file the obvious place to accidentally ship one. It is
        // re-readable through the provisioning endpoint and gets copied to the
        // clipboard, so the invariant has to hold in the ack-configured case too.
        (string snippet, string _) = BuildSnippet(AckFingerprint);

        AssertNoPrivateKeyBlock(snippet);
    }

    [Fact]
    public void ReportsTheAckKeyFingerprintWhenOneIsImported()
    {
        (string snippet, string _) = BuildSnippet(AckFingerprint);

        Assert.Contains($"SPKI-SHA256 {AckFingerprint}", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr bool kAckEnabled = true;", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysSoWhenNoAckKeyHasBeenImported()
    {
        // The state right after POST /api/devices. Silence here would be the worst
        // outcome: the operator would flash firmware that waits for acks the API is
        // not configured to send, and read the resulting retries as a bug.
        (string snippet, string _) = BuildSnippet(null);

        Assert.Contains("NOT YET CONFIGURED", snippet, StringComparison.Ordinal);
        Assert.Contains("--ack-public-pem GNSS01_ack_public.pem", snippet, StringComparison.Ordinal);

        // And the flag follows the key rather than being hard-coded on: acks enabled
        // against a server with no key to seal them means every fix waits out its
        // timeout, with nothing in the logs to explain why.
        Assert.Contains("constexpr bool kAckEnabled = false;", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersTheDevicesOwnSettingsAsTheCompileTimeDefaults()
    {
        // So a freshly flashed tracker behaves correctly on its first cycle, before
        // the broker has replayed the retained config document to it.
        (string snippet, string _) = BuildSnippet();

        Assert.Contains("kDefaultSendIntervalSeconds = 300;", snippet, StringComparison.Ordinal);
        Assert.Contains("kDefaultSleepBetweenSends   = true;", snippet, StringComparison.Ordinal);
        Assert.Contains("kFixAcquireTimeoutSeconds = 240;", snippet, StringComparison.Ordinal);
        Assert.Contains("kSdMaxQueuedFixes = 5000;", snippet, StringComparison.Ordinal);
        Assert.Contains("kRetryIntervalHours = 12;", snippet, StringComparison.Ordinal);
        Assert.Contains("kRetryMaxAgeHours = 72;", snippet, StringComparison.Ordinal);
        Assert.Contains("kDefaultConfigCheckSeconds = 1800;", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersTheApisOwnBoundsAsTheFirmwareClamps()
    {
        // The firmware clamps to these and the API rejects outside them. Rendering them
        // from DeviceConfigRules is what stops the two sides drifting apart.
        (string snippet, string _) = BuildSnippet();

        Assert.Contains(
            $"kMinSendIntervalSeconds = {DeviceConfigRules.MinIntervalSeconds};",
            snippet,
            StringComparison.Ordinal);
        Assert.Contains(
            $"kMaxSendIntervalSeconds = {DeviceConfigRules.MaxIntervalSeconds};",
            snippet,
            StringComparison.Ordinal);
        Assert.Contains(
            $"kMaxRetryMaxAgeHours = {DeviceConfigRules.MaxRetryMaxAgeHours};",
            snippet,
            StringComparison.Ordinal);
        Assert.Contains(
            $"kMinConfigCheckSeconds     = {DeviceConfigRules.MinConfigCheckSeconds};",
            snippet,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AckFingerprint)]
    public void NeverEndsACommentLineWithABackslash(string? ackPublicKeyFingerprint)
    {
        // A '\' at the end of a // line continues the comment onto the next line, and
        // the firmware builds with -Werror=comment — so one of these does not merely
        // look odd, it stops the whole file compiling. Wrapping a long shell command
        // that way is the obvious thing to do, which is exactly why it is pinned here:
        // the ack block used to do it, and only a real `pio run` caught it.
        (string snippet, string _) = BuildSnippet(ackPublicKeyFingerprint);

        string[] lines = snippet.Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            if (!lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.False(
                lines[index].EndsWith('\\'),
                $"Line {index + 1} is a comment ending in a backslash, which swallows the line after it: {lines[index]}");
        }
    }

    [Fact]
    public void UsesLfLineEndingsOnly()
    {
        (string snippet, string _) = BuildSnippet();

        // The file travels through JSON into a Git-tracked C++ file; CRLF would ride
        // along purely because the API happens to run on Windows — and a CR inside a
        // continued string literal is a compile error.
        Assert.DoesNotContain("\r", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlesACrLfPemUnchanged()
    {
        // A PEM that arrived with Windows line endings must still produce clean
        // literals — a stray CR inside a C string literal is a compile error.
        string crlfPem = TestKeys.ReceiverKey.ExportSubjectPublicKeyInfoPem().ReplaceLineEndings("\r\n");
        ConfigSnippetBuilder builder = new ConfigSnippetBuilder();

        string snippet = builder.Build(
            DeviceId,
            BrokerUri,
            crlfPem,
            Fingerprint,
            AckFingerprint,
            s_settings,
            s_generatedAt);

        Assert.DoesNotContain("\r", snippet, StringComparison.Ordinal);
        Assert.Contains("\"-----END PUBLIC KEY-----\\n\";\n", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void ProducesALiteralThatReassemblesIntoTheOriginalPem()
    {
        // The real contract: whatever the C compiler ends up with must be a PEM that
        // parses back into the same key. Undo the literal syntax and check.
        (string snippet, string publicKeyPem) = BuildSnippet();

        int start = snippet.IndexOf("constexpr char kReceiverPublicKeyPem[] =", StringComparison.Ordinal);
        string literalBlock = snippet[start..];
        string reassembled = string.Concat(literalBlock
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeWhile(static line => !line.StartsWith("// ---", StringComparison.Ordinal))
            .Where(static line => line.StartsWith('"'))
            .Select(static line => line.Trim(';').Trim('"').Replace("\\n", "\n", StringComparison.Ordinal)));

        using RSA reimported = RSA.Create();
        reimported.ImportFromPem(reassembled);

        Assert.Equal(3072, reimported.KeySize);
        Assert.Equal(
            publicKeyPem.ReplaceLineEndings("\n").TrimEnd('\n'),
            reassembled.TrimEnd('\n'));
    }

    /// <summary>
    /// Fails if the rendered file contains anything shaped like a private key PEM,
    /// in any of the encodings one could arrive in.
    /// </summary>
    /// <param name="snippet">The rendered file.</param>
    private static void AssertNoPrivateKeyBlock(string snippet)
    {
        Assert.DoesNotContain("-----BEGIN PRIVATE KEY-----", snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("-----BEGIN RSA PRIVATE KEY-----", snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("-----BEGIN ENCRYPTED PRIVATE KEY-----", snippet, StringComparison.Ordinal);

        // The literal form the firmware would carry it in, had one leaked into the
        // template's C string literal instead of a bare PEM.
        Assert.DoesNotContain("\"-----BEGIN PRIVATE KEY-----\\n\"", snippet, StringComparison.Ordinal);
    }
}
