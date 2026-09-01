using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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
    /// Every constant <see cref="ConfigSnippetBuilder"/> rewrites, paired with the
    /// value it must carry in a file rendered from <see cref="s_settings"/>.
    ///
    /// <para>
    /// This is not a list of what the firmware declares — that list used to live here,
    /// by hand, and it is precisely what failed: the firmware gained three
    /// fix-averaging constants, nobody added them, and the guard stayed green while the
    /// dashboard served a file that no longer compiled.
    /// <see cref="StagedTemplateMatchesFirmwareSource"/> covers completeness now, for
    /// free, by comparing the whole file.
    /// </para>
    ///
    /// <para>
    /// What is left for this list is the risk name-anchoring introduces in exchange: a
    /// constant the builder <em>rewrites</em> is one whose name it hard-codes, so a
    /// firmware rename would leave the placeholder value in place. That is the quiet
    /// failure — a tracker that builds cleanly and publishes to <c>devices/GNSSXX</c>.
    /// </para>
    /// </summary>
    private static readonly (string Constant, string Value)[] s_substitutedConstants =
    [
        // Identity and topics — the ones where a silent miss sends telemetry to the
        // wrong place rather than failing to compile.
        ("kDeviceId", "\"GNSS01\""),
        ("kTelemetryTopic", "\"devices/GNSS01\""),
        ("kConfigTopic", "\"devices/GNSS01/config\""),
        ("kAckTopic", "\"devices/GNSS01/ack\""),
        ("kMqttBrokerUri", $"\"{BrokerUri}\""),
        ("kMqttUsername", "\"GNSS01\""),
        ("kMqttClientId", "\"GNSS01\""),

        // Secrets, forced blank whatever the firmware template happens to carry.
        ("kWifiSsid", "\"\""),
        ("kWifiPassword", "\"\""),
        ("kMqttPassword", "\"\""),
        ("kDeviceAckPrivateKeyPem", "\"\""),
        ("kWifiEnabled", "false"),

        // This device's live settings as the compile-time defaults.
        ("kDefaultSendIntervalSeconds", "300"),
        ("kDefaultSleepBetweenSends", "true"),
        ("kFixAcquireTimeoutSeconds", "240"),
        ("kSdMaxQueuedFixes", "5000"),
        ("kRetryIntervalHours", "12"),
        ("kRetryMaxAgeHours", "72"),
        ("kDefaultConfigCheckSeconds", "1800"),

        // Bounds, read from the API's own rules rather than repeated as literals —
        // what is being checked here is that the anchor matched at all, not that
        // DeviceConfigRules holds a particular number.
        ("kMinSendIntervalSeconds", $"{DeviceConfigRules.MinIntervalSeconds}"),
        ("kMaxSendIntervalSeconds", $"{DeviceConfigRules.MaxIntervalSeconds}"),
        ("kMinFixTimeoutSeconds", $"{DeviceConfigRules.MinFixTimeoutSeconds}"),
        ("kMaxFixTimeoutSeconds", $"{DeviceConfigRules.MaxFixTimeoutSeconds}"),
        ("kMinQueueMaxFixes", $"{DeviceConfigRules.MinQueueMaxFixes}"),
        ("kMaxQueueMaxFixes", $"{DeviceConfigRules.MaxQueueMaxFixes}"),
        ("kMinRetryIntervalHours", $"{DeviceConfigRules.MinRetryIntervalHours}"),
        ("kMaxRetryIntervalHours", $"{DeviceConfigRules.MaxRetryIntervalHours}"),
        ("kMaxRetryMaxAgeHours", $"{DeviceConfigRules.MaxRetryMaxAgeHours}"),
        ("kMinConfigCheckSeconds", $"{DeviceConfigRules.MinConfigCheckSeconds}"),
        ("kMaxConfigCheckSeconds", $"{DeviceConfigRules.MaxConfigCheckSeconds}"),
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

        // Every constant the FIRMWARE TEMPLATE declares must survive rendering. The
        // list is derived from the template rather than typed out, so it cannot go
        // stale — and combined with StagedTemplateMatchesFirmwareSource below, "the
        // template declares it" and "the firmware declares it" are the same statement.
        MatchCollection declared = Regex.Matches(
            LoadStagedTemplate(),
            @"^constexpr\s+[A-Za-z_]\w*\s+(k[A-Za-z0-9_]+)",
            RegexOptions.Multiline);

        Assert.NotEmpty(declared);

        foreach (Match declaration in declared)
        {
            string constant = declaration.Groups[1].Value;

            Assert.True(
                Regex.IsMatch(snippet, $@"^constexpr\s+\S+\s+{constant}\b", RegexOptions.Multiline),
                $"The rendered Config.h declares no '{constant}', although the template does — a "
                    + "substitution swallowed the declaration instead of rewriting its value.");
        }

        // The template no longer has {{TOKEN}} holes, so one appearing means a stale
        // copy of the old hand-maintained file has been resurrected somehow.
        Assert.DoesNotContain("{{", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void StagedTemplateMatchesFirmwareSource()
    {
        // Pins the invariant the whole design rests on: the template embedded in this
        // assembly is a VERBATIM copy of ESP32/src/config/Config.example.h, not an
        // edited one. It is staged by an MSBuild target because the API image is built
        // with API/ as its Docker context and cannot see the firmware tree.
        //
        // Note what this does and does not catch. That same target re-stages the file
        // before this test runs, so on a developer's machine it cannot fail for
        // staleness — warning CARPOS001 in CarPosAPI.csproj is what shouts when the
        // committed copy was behind, and committing the refreshed file is what fixes
        // it. What this catches is the template being hand-edited back into a
        // {{TOKEN}} file, or the staging target being removed or silently failing —
        // either of which would put ConfigSnippetBuilder back where it started.
        //
        // Its predecessor was a hand-written list of firmware constant names, and it
        // let three of them through. Comparing the whole file needs no maintenance at
        // all and cannot miss one.
        string? firmwareSource = FindFirmwareConfigExample();

        if (firmwareSource is null)
        {
            // Absent inside the Docker build, where only API/ is copied into the
            // context. Failing there would make the image unbuildable for a reason
            // that has nothing to do with the image.
            return;
        }

        // LF on both sides: the working copy may be checked out CRLF on Windows, and
        // ConfigSnippetBuilder normalises what it loads for exactly that reason.
        string expected = File.ReadAllText(firmwareSource).ReplaceLineEndings("\n");
        string staged = LoadStagedTemplate();

        Assert.True(
            string.Equals(expected, staged, StringComparison.Ordinal),
            "API/CarPosAPI/Services/Provisioning/ConfigTemplate.h.txt is out of date with "
                + "ESP32/src/config/Config.example.h. It is generated: run `dotnet build` in API/ to "
                + "refresh it, then commit it alongside the firmware change. Never edit it by hand.");
    }

    [Fact]
    public void FillsInEverySubstitutedConstant()
    {
        // The risk name-anchored substitution buys in exchange for the drift guard
        // above: rewriting by name means the names are hard-coded here, so a firmware
        // rename could leave the template's placeholder in place. ConfigConstantWriter
        // throws rather than skipping, so this test is really asserting that the
        // builder's list of names still matches the firmware's.
        (string snippet, string _) = BuildSnippet(AckFingerprint);

        foreach ((string constant, string value) in s_substitutedConstants)
        {
            Assert.True(
                Regex.IsMatch(
                    snippet,
                    $@"^constexpr\s+\S+\s+{constant}(\[\])?\s*=\s*{Regex.Escape(value)}\s*;",
                    RegexOptions.Multiline),
                $"'{constant}' was not rendered as {value}. Either the firmware renamed or retyped it "
                    + "and ConfigSnippetBuilder needs the same edit, or a substitution wrote the wrong "
                    + "value — the first of those ships a tracker that talks to the template's "
                    + "placeholder topic.");
        }
    }

    [Fact]
    public void DropsAStaleHintFromAConstantItFillsIn()
    {
        // The firmware template ends kMqttBrokerUri with "<-- set in Config.h, leave
        // blank here" — advice for someone copying the file by hand, and the exact
        // opposite of what to do with the URI the API just filled in. Left in place it
        // reads as an instruction to delete a correct value.
        (string snippet, string _) = BuildSnippet();

        Assert.Contains($"constexpr char kMqttBrokerUri[] = \"{BrokerUri}\";\n", snippet, StringComparison.Ordinal);

        // But the same hints on the constants that DO stay blank are still true, and
        // are the operator's only in-file instruction to fill them in.
        Assert.Contains("constexpr char kWifiSsid[]     = \"\";  // <--", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kMqttPassword[] = \"\";  // <--", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void DropsAGlossFromANumberItChanges()
    {
        // The firmware annotates its less readable numbers with human units, attached
        // to the example's values. kDefaultConfigCheckSeconds ships as "3600;  // 1
        // hour"; rendered for a device set to 1800 it would otherwise read
        // "1800;  // 1 hour" — a file stating something false about itself, in a
        // comment, where nothing downstream can ever catch it.
        (string snippet, string _) = BuildSnippet();

        Assert.Contains("constexpr uint32_t kDefaultConfigCheckSeconds = 1800;\n", snippet, StringComparison.Ordinal);

        // A gloss on a number that came out unchanged is still true, and is the only
        // thing telling a reader that 86400 seconds is a day.
        Assert.Contains("kMaxConfigCheckSeconds     = 86400;  // 24 h", snippet, StringComparison.Ordinal);
    }

    /// <summary>Reads the template embedded in the API assembly, LF-normalised.</summary>
    /// <returns>The staged template text.</returns>
    private static string LoadStagedTemplate()
    {
        Assembly assembly = typeof(ConfigSnippetBuilder).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(
            "CarPosAPI.Services.Provisioning.ConfigTemplate.h.txt");

        Assert.NotNull(stream);

        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Walks up from <b>this source file</b> looking for the firmware's committed
    /// config template.
    ///
    /// <para>
    /// Anchored on the source path rather than <c>AppContext.BaseDirectory</c> on
    /// purpose: the binaries can be built anywhere (a <c>BaseOutputPath</c> outside the
    /// repo is how you build this solution while the API is running and holding a lock
    /// on <c>bin/</c>), and from there the walk finds nothing and the guard quietly
    /// skips itself — which is the one thing a drift guard must never do. The compiler
    /// bakes in the real source location, so this works from any output directory.
    /// </para>
    /// </summary>
    /// <param name="thisFile">Filled in by the compiler; never pass it.</param>
    /// <returns>The full path, or null when the firmware tree is not in this checkout.</returns>
    private static string? FindFirmwareConfigExample([CallerFilePath] string thisFile = "")
    {
        DirectoryInfo? directory = new DirectoryInfo(Path.GetDirectoryName(thisFile) ?? AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "ESP32", "src", "config", "Config.example.h");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
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
