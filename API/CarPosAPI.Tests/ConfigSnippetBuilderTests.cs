using System.Security.Cryptography;
using CarPosAPI.Services.Provisioning;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the snippet the provisioning endpoint hands out. The stakes are higher
/// than they look: a mistake in the PEM-to-C-string-literal conversion only
/// surfaces once the block has been pasted into <c>Config.h</c> and the firmware
/// refuses to compile — a long way from this code. These tests assert the exact
/// literal shape the firmware expects, and that the broker password is never
/// filled in (the API does not issue MQTT credentials).
/// </summary>
public sealed class ConfigSnippetBuilderTests
{
    private const string DeviceId = "GNSS01";

    private const string BrokerUri = "wss://jimajer.cz:443/mqttBroker";

    private const string Fingerprint = "ABCDEF0123456789";

    private static readonly DateTime s_generatedAt = new DateTime(2026, 7, 22, 10, 15, 0, DateTimeKind.Utc);

    private const string AckFingerprint = "0123456789ABCDEF";

    /// <summary>Builds a snippet for a real generated key, as the service does.</summary>
    /// <param name="ackPublicKeyFingerprint">
    /// Ack key on file, or null for a device that has none yet — the state every
    /// device is in immediately after <c>POST /api/devices</c>.
    /// </param>
    /// <returns>The rendered snippet and the PEM it was built from.</returns>
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
        Assert.Contains("constexpr char kMqttClientId[]   = \"GNSS01\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kTelemetryTopic[] = \"devices/GNSS01\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kConfigTopic[]    = \"devices/GNSS01/config\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kAckTopic[]       = \"devices/GNSS01/ack\";", snippet, StringComparison.Ordinal);
        Assert.Contains($"constexpr char kMqttBrokerUri[]  = \"{BrokerUri}\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kReceiverPublicKeyPem[] =", snippet, StringComparison.Ordinal);
        Assert.Contains("provisioned 2026-07-22T10:15:00Z", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesTheBrokerPasswordEmpty()
    {
        (string snippet, string _) = BuildSnippet();

        // The API never issues MQTT credentials — the account is created by hand
        // on the server. Emitting anything here would be a lie at best.
        Assert.Contains("constexpr char kMqttPassword[] = \"\";", snippet, StringComparison.Ordinal);
        Assert.Contains("constexpr char kMqttUsername[] = \"GNSS01\";", snippet, StringComparison.Ordinal);
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
        // The final literal carries the statement's semicolon and nothing follows it.
        Assert.EndsWith("\"-----END PUBLIC KEY-----\\n\";\n", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverLeaksPrivateKeyMaterial()
    {
        (string snippet, string _) = BuildSnippet();

        // The device only ever gets the public half; the private key stays
        // encrypted in the database. A regression here is a total compromise of
        // the end-to-end encryption, so it is asserted explicitly.
        Assert.DoesNotContain("PRIVATE", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverLeaksPrivateKeyMaterialWhenAnAckKeyIsOnFile()
    {
        // The ack direction inverts the key roles — the *device* holds that private
        // key — which makes this snippet the obvious place to accidentally ship one.
        // It is re-readable through the provisioning endpoint and gets copied to the
        // clipboard, so the invariant has to hold in the ack-configured case too.
        (string snippet, string _) = BuildSnippet(AckFingerprint);

        Assert.DoesNotContain("PRIVATE", snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN RSA", snippet, StringComparison.Ordinal);
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
    }

    [Fact]
    public void UsesLfLineEndingsOnly()
    {
        (string snippet, string _) = BuildSnippet();

        // The snippet travels through JSON into a Git-tracked C++ file; CRLF would
        // ride along purely because the API happens to run on Windows.
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
            s_generatedAt);

        Assert.DoesNotContain("\r", snippet, StringComparison.Ordinal);
        Assert.EndsWith("\"-----END PUBLIC KEY-----\\n\";\n", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void ProducesALiteralThatReassemblesIntoTheOriginalPem()
    {
        // The real contract: whatever the C compiler ends up with must be a PEM
        // that parses back into the same key. Undo the literal syntax and check.
        (string snippet, string publicKeyPem) = BuildSnippet();

        int start = snippet.IndexOf("constexpr char kReceiverPublicKeyPem[] =", StringComparison.Ordinal);
        string literalBlock = snippet[start..];
        string reassembled = string.Concat(literalBlock
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line.StartsWith('"'))
            .Select(static line => line.Trim(';').Trim('"').Replace("\\n", "\n", StringComparison.Ordinal)));

        using RSA reimported = RSA.Create();
        reimported.ImportFromPem(reassembled);

        Assert.Equal(3072, reimported.KeySize);
        Assert.Equal(
            publicKeyPem.ReplaceLineEndings("\n").TrimEnd('\n'),
            reassembled.TrimEnd('\n'));
    }
}
