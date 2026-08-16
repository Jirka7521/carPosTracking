using System.Text.Json;
using System.Text.Json.Serialization;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Publishes sealed delivery acks to <c>devices/&lt;id&gt;/ack</c>.
///
/// <para>
/// <b>Why this class holds a client reference.</b> The connected
/// <see cref="IMqttClient"/> lives and dies inside
/// <see cref="MqttIngestService"/>'s reconnect loop, so it cannot be registered in
/// DI. This singleton is handed the client on connect and has it cleared on
/// disconnect, which keeps exactly one connection in play — publishing an ack on
/// the same session that delivered the telemetry.
/// </para>
///
/// <para>
/// <b>Nothing here is allowed to fail loudly.</b> By the time an ack is published
/// the fixes are already committed; a crypto or transport problem must not turn a
/// successful ingest into a retryable failure and a redelivery storm. Every failure
/// path logs and returns.
/// </para>
/// </summary>
internal sealed class MqttAckPublisher : IAckPublisher
{
    /// <summary>
    /// Topic prefix for all device topics. Duplicated from
    /// <see cref="IngestPipeline"/> and <c>ConfigSnippetBuilder</c>, which already
    /// keep their own copies — the three must stay in step.
    /// </summary>
    private const string TopicPrefix = "devices/";

    /// <summary>Suffix identifying the delivery-ack topic for a device.</summary>
    private const string AckTopicSuffix = "/ack";

    /// <summary>
    /// Omit null members so the envelope on the wire carries only the five fields
    /// the firmware's parser expects — the optional <c>id</c> belongs to telemetry,
    /// not to acks.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IAckSealer _sealer;
    private readonly MqttOptions _options;
    private readonly ILogger<MqttAckPublisher> _logger;

    /// <summary>
    /// The live client, or null while disconnected. Written by the ingest service's
    /// connect loop and read by the message handler; <c>volatile</c> because those
    /// are different tasks and a stale cached read would publish onto a dead client.
    /// </summary>
    private volatile IMqttClient? _client;

    /// <summary>Creates the publisher.</summary>
    /// <param name="sealer">Encrypts the ack to the device's ack public key.</param>
    /// <param name="options">Broker settings, including the ack switch and QoS.</param>
    /// <param name="logger">Structured logger (never receives key material).</param>
    public MqttAckPublisher(
        IAckSealer sealer,
        IOptions<MqttOptions> options,
        ILogger<MqttAckPublisher> logger)
    {
        _sealer = sealer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public void AttachClient(IMqttClient? client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        DeviceKeyEntry device,
        IReadOnlyList<string> stored,
        IReadOnlyList<DeliveryAckRejectionDto> rejected,
        CancellationToken cancellationToken)
    {
        if (!_options.AckEnabled)
        {
            return;
        }

        // Nothing to confirm: every envelope in the message predated the ack
        // protocol, so there is no id to name and no ack worth sending.
        if (stored.Count == 0 && rejected.Count == 0)
        {
            return;
        }

        if (device.AckPublicKey is null)
        {
            _logger.LogDebug(
                "Skipping delivery ack for device {DeviceId} — no ack public key provisioned",
                device.DeviceId);
            return;
        }

        IMqttClient? client = _client;
        if (client is null || !client.IsConnected)
        {
            _logger.LogWarning(
                "Skipping delivery ack for device {DeviceId} — no live broker connection",
                device.DeviceId);
            return;
        }

        DeliveryAckDto ack = new DeliveryAckDto(device.DeviceId, stored, rejected);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(ack, s_jsonOptions);

        EncryptionEnvelopeDto? envelope = _sealer.TrySeal(device, plaintext);
        if (envelope is null)
        {
            // TrySeal already logged the reason.
            return;
        }

        string topic = string.Concat(TopicPrefix, device.DeviceId, AckTopicSuffix);
        MqttApplicationMessage message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(envelope, s_jsonOptions))
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)_options.AckQos)
            // Never retain: a retained ack would be replayed on every reconnect and
            // would confirm fixes from an old session that were never re-sent.
            .WithRetainFlag(false)
            .Build();

        // Bound the wait. This runs inside the message handler MQTTnet awaits before
        // reading further packets, so an unacknowledged PUBACK must not be able to
        // stall ingest indefinitely.
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.AckPublishTimeoutSeconds));

        try
        {
            await client.PublishAsync(message, timeout.Token);
            _logger.LogDebug(
                "Published delivery ack to {Topic}: {StoredCount} stored, {RejectedCount} rejected",
                topic,
                stored.Count,
                rejected.Count);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Delivery ack for device {DeviceId} timed out after {TimeoutSeconds}s — the device will retry",
                device.DeviceId,
                _options.AckPublishTimeoutSeconds);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — nothing to report, the device retries on reconnect.
        }
        catch (Exception exception)
        {
            // Deliberately broad. The fixes are already committed and the message is
            // about to be acknowledged to the broker; letting any transport fault
            // escape here would turn a successful ingest into a redelivery loop.
            // The cost of a swallowed ack is one device retry, which is by design.
            _logger.LogWarning(
                "Delivery ack for device {DeviceId} failed: {ExceptionType}",
                device.DeviceId,
                exception.GetType().Name);
        }
    }
}
