using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Orchestrates one message through the whole ingest chain and — critically —
/// classifies every possible failure as either "poison" (log, count, consume) or
/// "retryable" (make the broker redeliver). Poison must never trigger redelivery
/// (it would loop forever and clog the broker's in-flight window); a database
/// outage must never consume messages (they would be lost). All logging here is
/// privacy-preserving: device ids, counts and reasons — never coordinates,
/// timestamps of a person's movements, or key material.
/// </summary>
internal sealed partial class IngestPipeline : IIngestPipeline
{
    /// <summary>Topic prefix all device telemetry arrives under.</summary>
    private const string TopicPrefix = "devices/";

    /// <summary>
    /// Reject reason for an envelope that would not decrypt. Not a
    /// <see cref="PositionRejectReason"/> because it fails before there is a payload
    /// to judge, but it travels in the same reason vocabulary on the wire.
    /// </summary>
    private const string DecryptFailedReason = "DecryptFailed";

    /// <summary>
    /// Reject reason for a batch the database refused on a constraint. Server-side
    /// and potentially transient (a device row deactivated mid-flight, say), which is
    /// exactly why the firmware retries these rather than discarding them.
    /// </summary>
    private const string StorageRejectedReason = "StorageRejected";

    /// <summary>
    /// Strict parser for the decrypted inner payload (flat object, shallow depth).
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
    {
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 4,
    };

    private readonly IDeviceRegistry _deviceRegistry;
    private readonly EnvelopeCodec _codec;
    private readonly IPayloadCryptoService _crypto;
    private readonly PositionValidator _validator;
    private readonly IPositionWriter _writer;
    private readonly IAckPublisher _ackPublisher;
    private readonly MqttConnectionState _state;
    private readonly IngestOptions _options;
    private readonly ILogger<IngestPipeline> _logger;

    /// <summary>Creates the pipeline with its collaborators.</summary>
    /// <param name="deviceRegistry">Device/key cache.</param>
    /// <param name="codec">Envelope structural decoder.</param>
    /// <param name="crypto">Envelope decryptor.</param>
    /// <param name="validator">Semantic payload validator.</param>
    /// <param name="writer">Idempotent batch persister.</param>
    /// <param name="ackPublisher">Publishes the sealed delivery ack back to the device.</param>
    /// <param name="state">Shared counters for health reporting.</param>
    /// <param name="options">Retry configuration.</param>
    /// <param name="logger">Structured logger (no PII, no keys).</param>
    public IngestPipeline(
        IDeviceRegistry deviceRegistry,
        EnvelopeCodec codec,
        IPayloadCryptoService crypto,
        PositionValidator validator,
        IPositionWriter writer,
        IAckPublisher ackPublisher,
        MqttConnectionState state,
        IOptions<IngestOptions> options,
        ILogger<IngestPipeline> logger)
    {
        _deviceRegistry = deviceRegistry;
        _codec = codec;
        _crypto = crypto;
        _validator = validator;
        _writer = writer;
        _ackPublisher = ackPublisher;
        _state = state;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestOutcome> ProcessAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        _state.RecordMessage();

        string? topicDeviceId = TryParseDeviceId(topic);
        if (topicDeviceId is null)
        {
            _logger.LogWarning("Dropping message on unexpected topic {Topic}", topic);
            return IngestOutcome.Success;
        }

        DeviceKeyEntry? device = await _deviceRegistry.TryGetAsync(topicDeviceId, cancellationToken);
        if (device is null)
        {
            // Already logged (with negative caching) by the registry.
            return IngestOutcome.Success;
        }

        EnvelopeDecodeResult decodeResult = _codec.Decode(payload);
        if (decodeResult.FatalError is not null)
        {
            _logger.LogWarning(
                "Dropping message from device {DeviceId}: {Reason}",
                topicDeviceId,
                decodeResult.FatalError);
            _state.RecordOutcome(0, 0, 1);
            return IngestOutcome.Success;
        }

        // Decrypt + parse + validate each envelope; failures are counted per reason
        // and skipped so one poison envelope never sinks its batch-mates.
        //
        // Alongside the counters we keep the per-envelope verdict keyed by the
        // firmware's correlation id, which is what the delivery ack is built from.
        // Envelopes without an id (pre-ack firmware) and envelopes the codec rejected
        // structurally have no id to name, so they are counted but never acked — the
        // device simply falls back to its own retry timeout for those.
        List<ValidatedPosition> validated = new List<ValidatedPosition>(decodeResult.Envelopes.Count);
        List<string> storedIds = new List<string>(decodeResult.Envelopes.Count);
        List<DeliveryAckRejectionDto> rejections = new List<DeliveryAckRejectionDto>();
        Dictionary<string, int> rejectCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int rejected = decodeResult.RejectedEnvelopes;
        if (rejected > 0)
        {
            rejectCounts["Malformed"] = rejected;
        }

        DateTime utcNow = DateTime.UtcNow;
        foreach (DecodedEnvelope envelope in decodeResult.Envelopes)
        {
            if (!_crypto.TryDecrypt(device, envelope, out byte[] plaintext))
            {
                rejected++;
                CountReject(rejectCounts, DecryptFailedReason);
                RecordRejection(rejections, envelope.Id, DecryptFailedReason);
                continue;
            }

            PositionPayloadDto? payloadDto;
            try
            {
                payloadDto = JsonSerializer.Deserialize<PositionPayloadDto>(plaintext, s_jsonOptions);
            }
            catch (JsonException)
            {
                payloadDto = null;
            }

            if (payloadDto is null)
            {
                rejected++;
                CountReject(rejectCounts, nameof(PositionRejectReason.JsonInvalid));
                RecordRejection(rejections, envelope.Id, nameof(PositionRejectReason.JsonInvalid));
                continue;
            }

            bool isValid = _validator.TryValidate(
                payloadDto,
                topicDeviceId,
                utcNow,
                out ValidatedPosition? position,
                out PositionRejectReason reason);
            if (!isValid || position is null)
            {
                rejected++;
                CountReject(rejectCounts, reason.ToString());
                RecordRejection(rejections, envelope.Id, reason.ToString());
                continue;
            }

            validated.Add(position);
            if (envelope.Id is not null)
            {
                // Provisionally stored — promoted to the ack only once the write
                // below actually succeeds.
                storedIds.Add(envelope.Id);
            }
        }

        if (rejected > 0)
        {
            // One aggregated warning per message — informative for operators, and
            // impossible for a flood of bad envelopes to turn into a log flood.
            _logger.LogWarning(
                "Device {DeviceId}: rejected {Rejected} envelope(s): {Reasons}",
                topicDeviceId,
                rejected,
                string.Join(", ", rejectCounts.Select(pair => $"{pair.Key}={pair.Value}")));
        }

        if (validated.Count == 0)
        {
            _state.RecordOutcome(0, 0, rejected);
            // Still ack: the device needs to hear about rejections just as much as
            // about successes, otherwise it keeps re-sending fixes we will never take.
            await _ackPublisher.PublishAsync(device, Array.Empty<string>(), rejections, cancellationToken);
            return IngestOutcome.Success;
        }

        PositionWriteAttempt attempt = await TryWriteWithRetryAsync(device.Id, topicDeviceId, validated, cancellationToken);
        PositionWriteResult? writeResult = attempt.Result;
        if (writeResult is null)
        {
            _state.RecordOutcome(0, 0, rejected);
            // Deliberately no ack. The message is not acknowledged to the broker
            // either, so it will be redelivered — telling the device anything now
            // would invite it to drop fixes that were never written.
            return IngestOutcome.RetryableFailure;
        }

        if (attempt.Poisoned)
        {
            // The batch was discarded, so nothing in it is stored. Report every fix
            // as rejected instead, which routes them to the firmware's retry queue
            // rather than silently destroying them.
            foreach (string poisonedId in storedIds)
            {
                rejections.Add(new DeliveryAckRejectionDto(poisonedId, StorageRejectedReason));
            }

            storedIds.Clear();
        }

        _state.RecordOutcome(writeResult.Inserted, writeResult.Duplicates, rejected);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        _logger.LogInformation(
            "Device {DeviceId}: {Received} envelope(s) → inserted {Inserted}, duplicates {Duplicates}, rejected {Rejected} in {ElapsedMs} ms",
            topicDeviceId,
            decodeResult.Envelopes.Count + decodeResult.RejectedEnvelopes,
            writeResult.Inserted,
            writeResult.Duplicates,
            rejected,
            (long)elapsed.TotalMilliseconds);

        // Last, and only on the success path: tell the device what happened, so it can
        // clear its SD queue with confidence instead of trusting a broker-level ack.
        await _ackPublisher.PublishAsync(device, storedIds, rejections, cancellationToken);
        return IngestOutcome.Success;
    }

    /// <summary>
    /// Runs the batched insert with a bounded exponential retry. Distinguishes
    /// data errors from infrastructure errors: a PostgreSQL integrity violation
    /// (SQLSTATE class 23 — e.g. the device row vanished mid-flight) will fail
    /// identically on every redelivery, so it is dropped as poison; anything else
    /// (connection refused, timeout, failover) is worth redelivering.
    /// </summary>
    /// <param name="deviceRowId">Database id of the device row.</param>
    /// <param name="deviceId">MQTT device id, for logging only.</param>
    /// <param name="validated">The batch to persist.</param>
    /// <param name="cancellationToken">Application shutdown token.</param>
    /// <returns>
    /// The attempt outcome — see <see cref="PositionWriteAttempt"/> for why the
    /// poison case is flagged separately rather than folded into a zeroed result.
    /// </returns>
    private async Task<PositionWriteAttempt> TryWriteWithRetryAsync(
        Guid deviceRowId,
        string deviceId,
        IReadOnlyList<ValidatedPosition> validated,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= _options.DbRetryCount; attempt++)
        {
            try
            {
                PositionWriteResult written = await _writer.WriteBatchAsync(deviceRowId, validated, cancellationToken);
                return new PositionWriteAttempt(written, false);
            }
            catch (PostgresException exception) when (exception.SqlState.StartsWith("23", StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Device {DeviceId}: batch violates constraint {ConstraintName} ({SqlState}) — dropping as poison",
                    deviceId,
                    exception.ConstraintName,
                    exception.SqlState);
                return new PositionWriteAttempt(new PositionWriteResult(0, 0), true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                bool isLastAttempt = attempt == _options.DbRetryCount;
                _logger.LogWarning(
                    exception,
                    "Device {DeviceId}: database write attempt {Attempt}/{MaxAttempts} failed",
                    deviceId,
                    attempt,
                    _options.DbRetryCount);
                if (isLastAttempt)
                {
                    break;
                }

                // 2 s, 4 s, 8 s … — long enough to ride out a restart, bounded so the
                // broker connection is not starved for minutes.
                TimeSpan delay = TimeSpan.FromSeconds(
                    _options.DbRetryBaseDelaySeconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.LogError(
            "Device {DeviceId}: database unavailable after {MaxAttempts} attempts — requesting redelivery",
            deviceId,
            _options.DbRetryCount);
        return new PositionWriteAttempt(null, false);
    }

    /// <summary>Extracts and validates the device id from a telemetry topic.</summary>
    /// <param name="topic">The raw MQTT topic.</param>
    /// <returns>The device id, or null when the topic is not <c>devices/&lt;id&gt;</c>.</returns>
    private static string? TryParseDeviceId(string topic)
    {
        if (!topic.StartsWith(TopicPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string remainder = topic[TopicPrefix.Length..];

        // Exactly one segment (devices/+ can't match deeper topics, but the guard
        // must not rely on broker behaviour) of safe characters.
        if (remainder.Length == 0 || remainder.Contains('/', StringComparison.Ordinal) || !DeviceIdRegex().IsMatch(remainder))
        {
            return null;
        }

        return remainder;
    }

    /// <summary>Increments one reject-reason counter.</summary>
    /// <param name="counts">The per-message counter dictionary.</param>
    /// <param name="reason">Reason key.</param>
    private static void CountReject(Dictionary<string, int> counts, string reason)
    {
        counts.TryGetValue(reason, out int current);
        counts[reason] = current + 1;
    }

    /// <summary>
    /// Records a rejection for the delivery ack, when the envelope carried an id to
    /// name it by. An envelope without one still counts towards the aggregated log
    /// line; it just cannot be reported back, which is the expected behaviour for
    /// firmware that predates the ack protocol.
    /// </summary>
    /// <param name="rejections">The per-message rejection list being built.</param>
    /// <param name="envelopeId">The envelope's correlation id, or null.</param>
    /// <param name="reason">Reason name from the closed vocabulary.</param>
    private static void RecordRejection(
        List<DeliveryAckRejectionDto> rejections,
        string? envelopeId,
        string reason)
    {
        if (envelopeId is null)
        {
            return;
        }

        rejections.Add(new DeliveryAckRejectionDto(envelopeId, reason));
    }

    /// <summary>Allowed device-id shape — mirrors what the firmware can be flashed with.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex DeviceIdRegex();
}
