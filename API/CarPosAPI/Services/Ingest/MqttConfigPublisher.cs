using System.Text.Json;
using CarPosAPI.Data;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using CarPosAPI.Services.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Implements <see cref="IConfigPublisher"/>.
///
/// <para>
/// <b>Retain is the whole mechanism.</b> A device with <c>sleep_between</c> on is
/// awake for a handful of seconds per cycle and will essentially never be connected at
/// the moment somebody saves a setting. Publishing retained means the broker holds the
/// last document and replays it the instant the device subscribes, which it does on
/// every wake. Publishing this without the retain flag would appear to work in testing
/// (against a device that happens to stay awake) and fail in the field.
/// </para>
///
/// <para>
/// <b>Nothing here fails loudly</b>, for the same reason as
/// <see cref="MqttAckPublisher"/>: the revision row is committed before the publish is
/// attempted, so a broker problem must not surface as a failed save. It is logged, and
/// <see cref="RepublishAllAsync"/> on the next reconnect puts it right.
/// </para>
///
/// <para>
/// Singleton, so it reaches the database through an
/// <see cref="IDbContextFactory{TContext}"/> rather than a scoped context.
/// </para>
/// </summary>
internal sealed class MqttConfigPublisher : IConfigPublisher
{
    /// <summary>
    /// Topic prefix for all device topics. Duplicated from <see cref="IngestPipeline"/>,
    /// <see cref="MqttAckPublisher"/> and <c>ConfigSnippetBuilder</c>, which already keep
    /// their own copies — they must stay in step.
    /// </summary>
    private const string TopicPrefix = "devices/";

    /// <summary>Suffix identifying the settings topic for a device.</summary>
    private const string ConfigTopicSuffix = "/config";

    /// <summary>Suffix identifying the schedule-bundle topic for a device.</summary>
    private const string ScheduleTopicSuffix = "/schedule";

    /// <summary>
    /// QoS 1 for settings. The document is idempotent — applying the same revision
    /// twice changes nothing, and the firmware skips the card write when the values
    /// match — so at-least-once is the right trade, and QoS 2 would add a round trip
    /// for no benefit. QoS 0 would risk losing the publish entirely.
    /// </summary>
    private const MqttQualityOfServiceLevel ConfigQos = MqttQualityOfServiceLevel.AtLeastOnce;

    /// <summary>
    /// Upper bound on one publish, in seconds. Reuses no option of its own: unlike an
    /// ack this never runs inside the message handler, so the only thing it protects is
    /// a request thread waiting on an unresponsive broker.
    /// </summary>
    private const int PublishTimeoutSeconds = 10;

    private readonly IDbContextFactory<CarPosDbContext> _contextFactory;
    private readonly ScheduleBundleBuilder _bundleBuilder;
    private readonly MqttOptions _options;
    private readonly ILogger<MqttConfigPublisher> _logger;

    /// <summary>
    /// The live client, or null while disconnected. <c>volatile</c> because it is
    /// written by the ingest service's connect loop and read from request threads.
    /// </summary>
    private volatile IMqttClient? _client;

    /// <summary>Creates the publisher.</summary>
    /// <param name="contextFactory">Factory for short-lived DbContexts (singleton-safe).</param>
    /// <param name="bundleBuilder">Assembles schedule bundles for the reconnect sweep.</param>
    /// <param name="options">Broker settings.</param>
    /// <param name="logger">Structured logger.</param>
    public MqttConfigPublisher(
        IDbContextFactory<CarPosDbContext> contextFactory,
        ScheduleBundleBuilder bundleBuilder,
        IOptions<MqttOptions> options,
        ILogger<MqttConfigPublisher> logger)
    {
        _contextFactory = contextFactory;
        _bundleBuilder = bundleBuilder;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public void AttachClient(IMqttClient? client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<bool> PublishAsync(
        string deviceId,
        DeviceConfigDocumentDto document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        return await PublishRetainedAsync(
            deviceId,
            ConfigTopicSuffix,
            JsonSerializer.SerializeToUtf8Bytes(document),
            "config",
            document.Version,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> PublishScheduleAsync(
        string deviceId,
        DeviceScheduleBundleDto bundle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return await PublishRetainedAsync(
            deviceId,
            ScheduleTopicSuffix,
            JsonSerializer.SerializeToUtf8Bytes(bundle, DeviceScheduleBundleDto.SerializerOptions),
            "schedule bundle",
            bundle.ScheduleVersion,
            cancellationToken);
    }

    /// <summary>
    /// Puts one already-serialized payload on one of this device's retained topics.
    ///
    /// Shared by both publishes above because everything except the topic and the
    /// payload is identical between them — the retain flag, the QoS, the timeout, and
    /// above all the four catch clauses that make a transport fault a log line instead
    /// of a failed request. Two copies of that error handling would be two chances to
    /// let an exception escape into a caller that has already committed its rows.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="topicSuffix">Which of the device's topics to publish to.</param>
    /// <param name="payload">The serialized document.</param>
    /// <param name="what">Human-readable name of the payload, for the logs.</param>
    /// <param name="version">Revision number of the payload, for the logs.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    /// <returns>True when the broker accepted it.</returns>
    private async Task<bool> PublishRetainedAsync(
        string deviceId,
        string topicSuffix,
        byte[] payload,
        string what,
        int version,
        CancellationToken cancellationToken)
    {
        IMqttClient? client = _client;
        if (client is null || !client.IsConnected)
        {
            // Not fatal, and not even unusual during a restart. The saved rows are safe
            // in the database and the reconnect sweep will publish them.
            _logger.LogWarning(
                "The {What} for device {DeviceId} was not published — no live broker connection; it will go out on reconnect",
                what,
                deviceId);
            return false;
        }

        string topic = string.Concat(TopicPrefix, deviceId, topicSuffix);
        MqttApplicationMessage message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(ConfigQos)
            .WithRetainFlag(true)
            .Build();

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(PublishTimeoutSeconds));

        try
        {
            await client.PublishAsync(message, timeout.Token);
            _logger.LogInformation(
                "Published {What} v{Version} to {Topic} (retained, {ByteCount} bytes)",
                what,
                version,
                topic,
                payload.Length);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Publishing the {What} for device {DeviceId} timed out after {TimeoutSeconds}s",
                what,
                deviceId,
                PublishTimeoutSeconds);
            return false;
        }
        catch (OperationCanceledException)
        {
            // Request aborted or host shutting down — nothing to report.
            return false;
        }
        catch (Exception exception)
        {
            // Deliberately broad, for the reason in the class summary: the rows are
            // already saved, and no transport fault may turn that into a failed request.
            _logger.LogWarning(
                "Publishing the {What} for device {DeviceId} failed: {ExceptionType}",
                what,
                deviceId,
                exception.GetType().Name);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> RepublishAllAsync(CancellationToken cancellationToken)
    {
        IMqttClient? client = _client;
        if (client is null || !client.IsConnected)
        {
            return 0;
        }

        List<DeviceConfigPublication> pending;
        List<DeviceScheduleBundlePublication> pendingBundles;
        try
        {
            // One query for the whole fleet: join each active device to the version row
            // it points at, and project straight to the document. A per-device lookup
            // here would be an N+1 that runs on every single reconnect.
            await using CarPosDbContext context =
                await _contextFactory.CreateDbContextAsync(cancellationToken);

            pending = await context.Devices
                .AsNoTracking()
                .Where(device => device.IsActive)
                .Join(
                    context.DeviceConfigVersions.AsNoTracking(),
                    device => new { DeviceRowId = device.Id, Version = device.ConfigVersion },
                    configVersion => new { DeviceRowId = configVersion.DeviceId, configVersion.Version },
                    (device, configVersion) => new DeviceConfigPublication(
                        device.DeviceId,
                        new DeviceConfigDocumentDto(
                            configVersion.Version,
                            configVersion.IntervalSeconds,
                            configVersion.SleepBetween,
                            configVersion.FixTimeoutSeconds,
                            configVersion.QueueMaxFixes,
                            configVersion.RetryIntervalHours,
                            configVersion.RetryMaxAgeHours,
                            configVersion.ConfigCheckSeconds)))
                .ToListAsync(cancellationToken);

            pendingBundles = await _bundleBuilder.BuildAllAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            // This runs on the ingest connect path. A database that is down must not
            // stop the broker connection from coming up — telemetry would then be lost
            // as well as settings being stale, which is strictly worse. The sweep is
            // retried on the next reconnect anyway.
            _logger.LogWarning(
                exception,
                "Could not load device configurations to re-publish; retained settings may be stale");
            return 0;
        }

        int published = 0;
        foreach (DeviceConfigPublication publication in pending)
        {
            if (await PublishAsync(publication.DeviceId, publication.Document, cancellationToken))
            {
                published++;
            }
        }

        int bundlesPublished = 0;
        foreach (DeviceScheduleBundlePublication publication in pendingBundles)
        {
            if (await PublishScheduleAsync(publication.DeviceId, publication.Bundle, cancellationToken))
            {
                bundlesPublished++;
            }
        }

        if (published > 0 || bundlesPublished > 0)
        {
            _logger.LogInformation(
                "Re-published retained config for {PublishedCount} of {DeviceCount} active device(s), and {BundleCount} schedule bundle(s)",
                published,
                pending.Count,
                bundlesPublished);
        }

        return published;
    }
}
