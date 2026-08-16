using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Options;

/// <summary>
/// Connection settings for the MQTT broker the ingest service subscribes to.
/// Bound from the <c>Mqtt</c> configuration section and validated at startup so a
/// misconfigured deployment fails fast instead of silently never receiving data.
/// The password is a secret and must come from user-secrets (dev) or an
/// environment variable (prod) — never from a tracked appsettings file.
/// </summary>
public sealed class MqttOptions
{
    /// <summary>Configuration section name this class binds to.</summary>
    public const string SectionName = "Mqtt";

    /// <summary>
    /// The one broker address, e.g. <c>ws://mqtt.local:9001/</c> in the deployed
    /// stack or <c>wss://jimajer.cz:443/mqttBroker</c> in development. It does two
    /// jobs: it is what this API dials, and it is what device provisioning writes
    /// into the firmware snippet as <c>kMqttBrokerUri</c>.
    /// <para>
    /// <b>Plaintext schemes are permitted.</b> The deployment reaches Mosquitto
    /// directly over the host's container network, so requiring TLS here would
    /// force the API out to the public reverse proxy and back for a hop that never
    /// leaves the machine. The telemetry itself is end-to-end encrypted by the
    /// firmware (RSA-OAEP + AES-GCM), so the transport carries ciphertext either
    /// way; what a plaintext hop does expose is the broker password.
    /// </para>
    /// <para>
    /// Because the same value is handed to devices, an address that only resolves
    /// inside the container network cannot be reached by a device — such a
    /// deployment must fix up <c>kMqttBrokerUri</c> by hand. See
    /// <see cref="HasSupportedBrokerUri"/>.
    /// </para>
    /// </summary>
    [Required]
    public string BrokerUri { get; set; } = string.Empty;

    /// <summary>Broker account the API authenticates as (read access to device topics).</summary>
    [Required]
    public string Username { get; set; } = string.Empty;

    /// <summary>Broker account password. Secret — user-secrets / environment only.</summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Stable MQTT client id. Must stay constant across restarts: the persistent
    /// session (clean_session=false) that buffers QoS 2 messages while the API is
    /// down is keyed by this id. Running two instances with the same id makes the
    /// broker kick them off each other — exactly one API instance may run.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = "carpos-api";

    /// <summary>
    /// Topic filter for device telemetry. <c>devices/+</c> matches exactly one id
    /// segment, so config topics (<c>devices/x/config</c>) are never received here.
    /// </summary>
    [Required]
    public string TopicFilter { get; set; } = "devices/+";

    /// <summary>
    /// Keep-alive interval. 30 s keeps the WebSocket alive through the reverse
    /// proxy in front of the broker, whose idle timeout would otherwise drop it.
    /// </summary>
    [Range(5, 300)]
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>
    /// Master switch for publishing delivery acks to <c>devices/&lt;id&gt;/ack</c>.
    /// Turning it off restores the pre-ack behaviour exactly: fixes are still
    /// ingested, devices simply never hear back and fall back to their own retry
    /// timeout. Useful as a kill switch if the broker ACL is wrong.
    /// </summary>
    public bool AckEnabled { get; set; } = true;

    /// <summary>
    /// QoS for the ack publish. 1 (at-least-once) is the right level: the device
    /// keys everything on envelope ids, so a duplicate ack is idempotent, while
    /// QoS 0 would silently lose acks and QoS 2 would add a round trip for nothing.
    /// </summary>
    [Range(0, 2)]
    public int AckQos { get; set; } = 1;

    /// <summary>
    /// Upper bound on one ack publish. The ack is sent from inside the message
    /// handler, which MQTTnet awaits before it processes further incoming packets —
    /// so an unbounded wait for a PUBACK could wedge ingest behind its own reply.
    /// This timeout makes that failure mode a logged warning instead of a stall.
    /// </summary>
    [Range(1, 60)]
    public int AckPublishTimeoutSeconds { get; set; } = 5;

    /// <summary>Initial reconnect delay; doubles (with jitter) up to the max below.</summary>
    [Range(1, 3600)]
    public int ReconnectMinDelaySeconds { get; set; } = 1;

    /// <summary>Upper bound for the exponential reconnect backoff.</summary>
    [Range(1, 3600)]
    public int ReconnectMaxDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Validates that <see cref="BrokerUri"/> is an absolute URI using one of the
    /// transports MQTTnet and the firmware both understand. Called from the options
    /// pipeline in <c>Program.cs</c>, so a violation aborts startup.
    /// <para>
    /// This deliberately no longer requires TLS. The deployed API reaches Mosquitto
    /// over the host's container network, where insisting on <c>wss</c> would mean
    /// routing out through the public proxy and back for a hop that never leaves the
    /// machine. Restricting the set to these four still means a typo like
    /// <c>http://</c> fails at startup rather than surfacing as a warning inside the
    /// reconnect loop.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> when the URI is absolute and uses ws/wss/mqtt/mqtts.</returns>
    public bool HasSupportedBrokerUri()
    {
        bool parsed = Uri.TryCreate(BrokerUri, UriKind.Absolute, out Uri? uri);
        if (!parsed || uri is null)
        {
            return false;
        }

        return string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "mqtt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "mqtts", StringComparison.OrdinalIgnoreCase);
    }
}
