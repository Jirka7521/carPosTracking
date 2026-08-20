using System.Buffers;
using CarPosAPI.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// The hosted MQTT consumer: owns the broker connection for the application's
/// lifetime, resubscribes after every (re)connect and feeds each message to
/// <see cref="IIngestPipeline"/> strictly sequentially (MQTTnet awaits the
/// handler before processing the next packet, which is also what makes
/// ack-after-processing work: the QoS 2 flow for a message only completes after
/// the handler returns with <c>AutoAcknowledge</c> left on).
///
/// Deliberate protocol choices, mirroring the device fleet:
/// MQTT 3.1.1 + clean_session=false + a stable client id give this consumer a
/// persistent broker session, so QoS 2 messages published while the API is down
/// are queued and delivered on reconnect. MQTTnet v5 has no managed client, so
/// reconnecting is a hand-rolled loop with exponential backoff and jitter.
/// The handler must never throw: an unacknowledged QoS 2 message permanently
/// occupies one of the broker's ~20 in-flight slots until reconnect, so every
/// failure is either consumed (poison) or answered with a deliberate
/// disconnect-and-redeliver cycle (database outage).
/// </summary>
internal sealed class MqttIngestService : BackgroundService
{
    /// <summary>Poll interval of the supervision loop while connected.</summary>
    private static readonly TimeSpan s_supervisionInterval = TimeSpan.FromSeconds(1);

    /// <summary>Grace period for the clean DISCONNECT on shutdown.</summary>
    private static readonly TimeSpan s_disconnectTimeout = TimeSpan.FromSeconds(5);

    private readonly MqttOptions _mqttOptions;
    private readonly IngestOptions _ingestOptions;
    private readonly IIngestPipeline _pipeline;
    private readonly IAckPublisher _ackPublisher;
    private readonly IConfigPublisher _configPublisher;
    private readonly MqttConnectionState _state;
    private readonly ILogger<MqttIngestService> _logger;

    /// <summary>Set by the message handler to demand a disconnect/redeliver cycle.</summary>
    private volatile bool _reconnectRequested;

    /// <summary>Shutdown token captured for use inside the message handler.</summary>
    private CancellationToken _stoppingToken;

    /// <summary>Current reconnect backoff; doubles per failure, resets on success.</summary>
    private double _reconnectDelaySeconds;

    /// <summary>Creates the service.</summary>
    /// <param name="mqttOptions">Broker connection settings.</param>
    /// <param name="ingestOptions">Retry/pause tuning.</param>
    /// <param name="pipeline">The message processing pipeline.</param>
    /// <param name="ackPublisher">Given the live client so the pipeline can reply to devices.</param>
    /// <param name="configPublisher">Given the live client so settings can be published retained.</param>
    /// <param name="state">Shared connection state for health reporting.</param>
    /// <param name="logger">Structured logger.</param>
    public MqttIngestService(
        IOptions<MqttOptions> mqttOptions,
        IOptions<IngestOptions> ingestOptions,
        IIngestPipeline pipeline,
        IAckPublisher ackPublisher,
        IConfigPublisher configPublisher,
        MqttConnectionState state,
        ILogger<MqttIngestService> logger)
    {
        _mqttOptions = mqttOptions.Value;
        _ingestOptions = ingestOptions.Value;
        _pipeline = pipeline;
        _ackPublisher = ackPublisher;
        _configPublisher = configPublisher;
        _state = state;
        _logger = logger;
        _reconnectDelaySeconds = _mqttOptions.ReconnectMinDelaySeconds;
    }

    /// <summary>Runs the connect/supervise/reconnect loop until shutdown.</summary>
    /// <param name="stoppingToken">Application shutdown token.</param>
    /// <returns>Completes when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        MqttClientFactory factory = new MqttClientFactory();
        using IMqttClient client = factory.CreateMqttClient();

        // Attach before the first connect: a persistent session can start delivering
        // queued messages immediately after CONNACK.
        client.ApplicationMessageReceivedAsync += HandleApplicationMessageReceivedAsync;
        client.DisconnectedAsync += HandleDisconnectedAsync;

        // Hand the pipeline's ack publisher this client for the client's whole
        // lifetime. It is deliberately not re-attached per reconnect: the same
        // instance is reused across them, and the publisher checks IsConnected
        // itself, so there is no window where it holds a client that no longer exists.
        _ackPublisher.AttachClient(client);
        _configPublisher.AttachClient(client);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_reconnectRequested)
                    {
                        // A database outage was detected mid-message. Drop the link so
                        // the broker redelivers the unacknowledged message, and pause so
                        // a down database is not hammered in a tight loop.
                        _reconnectRequested = false;
                        await DisconnectQuietlyAsync(client);
                        _logger.LogWarning(
                            "Pausing {DelaySeconds} s before reconnect after a database failure",
                            _ingestOptions.DbFailureReconnectDelaySeconds);
                        await Task.Delay(
                            TimeSpan.FromSeconds(_ingestOptions.DbFailureReconnectDelaySeconds),
                            stoppingToken);
                    }

                    if (!client.IsConnected)
                    {
                        await ConnectAndSubscribeAsync(client, stoppingToken);
                    }

                    await Task.Delay(s_supervisionInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown — leave the loop; the finally below disconnects cleanly.
                    break;
                }
                catch (Exception exception)
                {
                    _state.SetConnected(false);
                    double jitteredSeconds = _reconnectDelaySeconds
                        * (1.0 + (0.2 * Random.Shared.NextDouble()));
                    _logger.LogWarning(
                        exception,
                        "MQTT connection attempt failed; retrying in {DelaySeconds:F1} s",
                        jitteredSeconds);
                    _reconnectDelaySeconds = Math.Min(
                        _reconnectDelaySeconds * 2,
                        _mqttOptions.ReconnectMaxDelaySeconds);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(jitteredSeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            // Clean DISCONNECT keeps the broker-side persistent session (and its
            // queued messages) intact for the next start.
            await DisconnectQuietlyAsync(client);
            _state.SetConnected(false);

            // Release the client before the `using` disposes it, so a late ack or
            // config publish cannot touch a disposed instance.
            _ackPublisher.AttachClient(null);
            _configPublisher.AttachClient(null);
        }
    }

    /// <summary>Connects, verifies the CONNACK, subscribes and verifies the SUBACK.</summary>
    /// <param name="client">The MQTT client.</param>
    /// <param name="cancellationToken">Shutdown token.</param>
    private async Task ConnectAndSubscribeAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        MqttClientOptions clientOptions = new MqttClientOptionsBuilder()
            // In the deployed stack this is the broker's address on the container
            // network, so ingest never leaves the host. The same value is what
            // device provisioning hands to firmware — see MqttOptions.BrokerUri.
            .WithConnectionUri(new Uri(_mqttOptions.BrokerUri, UriKind.Absolute))
            // MQTT 3.1.1 to match the broker/device ecosystem; v5 sessions would add
            // an expiry-interval failure mode for zero benefit here.
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId(_mqttOptions.ClientId)
            .WithCredentials(_mqttOptions.Username, _mqttOptions.Password)
            // The persistent session is what buffers QoS 2 messages while we're down.
            .WithCleanSession(false)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_mqttOptions.KeepAliveSeconds))
            .Build();

        // MQTTnet v5: a refused connection returns a result code instead of throwing.
        MqttClientConnectResult connectResult = await client.ConnectAsync(clientOptions, cancellationToken);
        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException(
                $"Broker refused the connection: {connectResult.ResultCode}.");
        }

        _logger.LogInformation(
            "Connected to MQTT broker as {ClientId} (sessionPresent={SessionPresent})",
            _mqttOptions.ClientId,
            connectResult.IsSessionPresent);

        // Always resubscribe — idempotent, and sessionPresent must not be trusted
        // blindly (a broker restart without persistence silently forgets us).
        MqttClientSubscribeOptions subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(
                new MqttTopicFilterBuilder()
                    .WithTopic(_mqttOptions.TopicFilter)
                    // QoS 2 end to end: the device publishes at 2; subscribing lower
                    // would downgrade delivery and multiply duplicates.
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .Build())
            .Build();

        MqttClientSubscribeResult subscribeResult = await client.SubscribeAsync(subscribeOptions, cancellationToken);
        foreach (MqttClientSubscribeResultItem item in subscribeResult.Items)
        {
            if (item.ResultCode != MqttClientSubscribeResultCode.GrantedQoS2)
            {
                // Reachable but wrong: almost always a broker-ACL problem. Known
                // failure mode of this broker (2026-07-03): SUBACK can even succeed
                // while delivery is silently filtered — see README verification steps.
                _logger.LogError(
                    "Subscription to {TopicFilter} was not granted QoS 2 (result: {ResultCode}) — check broker ACL for user {Username}",
                    _mqttOptions.TopicFilter,
                    item.ResultCode,
                    _mqttOptions.Username);
            }
        }

        _state.SetConnected(true);
        _reconnectDelaySeconds = _mqttOptions.ReconnectMinDelaySeconds;
        _logger.LogInformation("Subscribed to {TopicFilter} at QoS 2", _mqttOptions.TopicFilter);

        // Refresh every device's retained settings document. Retained messages normally
        // outlive us on the broker, but one restarted without persistence forgets them
        // silently — and a deep-sleeping device, online for seconds at a time, would
        // then never learn its configuration again. Doing it here makes that
        // self-healing. Failures are logged inside the publisher and must not abort the
        // connect: ingest matters more than settings.
        await _configPublisher.RepublishAllAsync(cancellationToken);
    }

    /// <summary>
    /// Handles one inbound message. Never throws: MQTTnet skips the acknowledge
    /// step when the handler faults, which would pin the message in the broker's
    /// in-flight window — instead every failure path decides its fate explicitly.
    /// </summary>
    /// <param name="eventArgs">Message event args.</param>
    private async Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        try
        {
            if (eventArgs.ApplicationMessage.Retain)
            {
                // Devices publish telemetry with retain=0; a retained message here
                // means someone published junk manually. Consume and ignore it.
                _logger.LogWarning(
                    "Ignoring unexpected retained message on {Topic}",
                    eventArgs.ApplicationMessage.Topic);
                return;
            }

            byte[] payload = eventArgs.ApplicationMessage.Payload.ToArray();
            IngestOutcome outcome = await _pipeline.ProcessAsync(
                eventArgs.ApplicationMessage.Topic,
                payload,
                _stoppingToken);

            if (outcome == IngestOutcome.RetryableFailure)
            {
                // Leave the message unacknowledged and cycle the connection so the
                // broker redelivers it once the pause elapses.
                eventArgs.AutoAcknowledge = false;
                _reconnectRequested = true;
            }
        }
        catch (Exception exception)
        {
            eventArgs.AutoAcknowledge = false;
            _reconnectRequested = true;
            _logger.LogError(
                exception,
                "Unhandled ingest failure on {Topic} — message left for redelivery",
                eventArgs.ApplicationMessage.Topic);
        }
    }

    /// <summary>Logs broker-initiated disconnects; the supervision loop reconnects.</summary>
    /// <param name="eventArgs">Disconnect event args.</param>
    private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs)
    {
        _state.SetConnected(false);
        if (!_stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "MQTT connection lost ({Reason}); the supervision loop will reconnect",
                eventArgs.Reason);
        }

        return Task.CompletedTask;
    }

    /// <summary>Best-effort clean disconnect (bounded, never throws).</summary>
    /// <param name="client">The MQTT client.</param>
    private async Task DisconnectQuietlyAsync(IMqttClient client)
    {
        if (!client.IsConnected)
        {
            return;
        }

        try
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(s_disconnectTimeout);
            await client.DisconnectAsync(
                new MqttClientDisconnectOptions(),
                timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Clean MQTT disconnect failed; continuing");
        }
    }
}
