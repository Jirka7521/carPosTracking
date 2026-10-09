using System.Text.Json;
using CarPosAPI.Dtos;
using CarPosAPI.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Runs one status message through the chain the position pipeline already trusts —
/// the same device registry, envelope codec and decryption, so a status message is
/// exactly as authenticated as a fix — and stores what it says.
///
/// <para>
/// Same failure classification as <see cref="IngestPipeline"/>: anything wrong with the
/// message itself is poison (log, consume), a database outage is retryable (leave it
/// for the broker to redeliver). There is <b>no delivery ack</b>: nothing on the device
/// is waiting to hear about a status message, and it keeps no copy to clear.
/// </para>
///
/// <para>
/// Logging stays privacy-preserving and proportionate: device id, kind and reason only.
/// Routine events (a device going to sleep every few minutes) log at Debug, so they do
/// not drown the log; alerts and errors log at Information, because those are the ones
/// an operator reading it is looking for.
/// </para>
/// </summary>
internal sealed class StatusIngestPipeline : IStatusIngestPipeline
{
    /// <summary>Strict parser for the decrypted inner payload (flat object, shallow depth).</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
    {
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 4,
    };

    private readonly IDeviceRegistry _deviceRegistry;
    private readonly EnvelopeCodec _codec;
    private readonly IPayloadCryptoService _crypto;
    private readonly DeviceStatusValidator _validator;
    private readonly IDeviceStatusWriter _writer;
    private readonly MqttConnectionState _state;
    private readonly IngestOptions _options;
    private readonly ILogger<StatusIngestPipeline> _logger;

    /// <summary>Creates the pipeline with its collaborators.</summary>
    /// <param name="deviceRegistry">Device/key cache, shared with position ingest.</param>
    /// <param name="codec">Envelope structural decoder.</param>
    /// <param name="crypto">Envelope decryptor.</param>
    /// <param name="validator">Validates and classifies the decrypted payload.</param>
    /// <param name="writer">Persists the event and the online time.</param>
    /// <param name="state">Shared counters for health reporting.</param>
    /// <param name="options">Retry configuration.</param>
    /// <param name="logger">Structured logger (device ids and reasons only).</param>
    public StatusIngestPipeline(
        IDeviceRegistry deviceRegistry,
        EnvelopeCodec codec,
        IPayloadCryptoService crypto,
        DeviceStatusValidator validator,
        IDeviceStatusWriter writer,
        MqttConnectionState state,
        IOptions<IngestOptions> options,
        ILogger<StatusIngestPipeline> logger)
    {
        _deviceRegistry = deviceRegistry;
        _codec = codec;
        _crypto = crypto;
        _validator = validator;
        _writer = writer;
        _state = state;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IngestOutcome> ProcessAsync(
        string deviceId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        _state.RecordMessage();

        DeviceKeyEntry? device = await _deviceRegistry.TryGetAsync(deviceId, cancellationToken);
        if (device is null)
        {
            // Already logged (with negative caching) by the registry.
            return IngestOutcome.Success;
        }

        EnvelopeDecodeResult decodeResult = _codec.Decode(payload);
        if (decodeResult.FatalError is not null)
        {
            _logger.LogWarning(
                "Dropping status message from device {DeviceId}: {Reason}",
                deviceId,
                decodeResult.FatalError);
            _state.RecordOutcome(0, 0, 1);
            return IngestOutcome.Success;
        }

        // The firmware sends exactly one envelope per status message, but the array
        // shape is shared with position batches, so every envelope is honoured.
        List<ValidatedDeviceStatus> validated = new List<ValidatedDeviceStatus>(decodeResult.Envelopes.Count);
        int rejected = decodeResult.RejectedEnvelopes;
        DateTime utcNow = DateTime.UtcNow;

        foreach (DecodedEnvelope envelope in decodeResult.Envelopes)
        {
            if (!_crypto.TryDecrypt(device, envelope, out byte[] plaintext))
            {
                rejected++;
                _logger.LogWarning("Device {DeviceId}: status envelope did not decrypt", deviceId);
                continue;
            }

            DeviceStatusPayloadDto? payloadDto;
            try
            {
                payloadDto = JsonSerializer.Deserialize<DeviceStatusPayloadDto>(plaintext, s_jsonOptions);
            }
            catch (JsonException)
            {
                payloadDto = null;
            }

            if (payloadDto is null)
            {
                rejected++;
                _logger.LogWarning(
                    "Device {DeviceId}: status rejected ({Reason})",
                    deviceId,
                    DeviceStatusRejectReason.JsonInvalid);
                continue;
            }

            bool isValid = _validator.TryValidate(
                payloadDto,
                deviceId,
                utcNow,
                out ValidatedDeviceStatus? status,
                out DeviceStatusRejectReason reason);
            if (!isValid || status is null)
            {
                rejected++;
                _logger.LogWarning("Device {DeviceId}: status rejected ({Reason})", deviceId, reason);
                continue;
            }

            validated.Add(status);
        }

        _state.RecordOutcome(0, 0, rejected);

        foreach (ValidatedDeviceStatus status in validated)
        {
            bool stored = await TryWriteWithRetryAsync(device.Id, deviceId, status, cancellationToken);
            if (!stored)
            {
                // Nothing is acknowledged, so the broker redelivers the whole message —
                // which may repeat an event this loop already stored. Accepted: see
                // DeviceStatusWriter on why status rows are not deduplicated.
                return IngestOutcome.RetryableFailure;
            }

            LogStored(deviceId, status);
        }

        return IngestOutcome.Success;
    }

    /// <summary>
    /// Writes one status with the same bounded exponential retry as position ingest.
    /// A PostgreSQL integrity violation (SQLSTATE class 23 — say, the device row was
    /// deleted mid-flight) would fail the same way on every redelivery, so it is
    /// dropped as poison; anything else is worth redelivering.
    /// </summary>
    /// <param name="deviceRowId">Database id of the device row.</param>
    /// <param name="deviceId">MQTT device id, for logging only.</param>
    /// <param name="status">The validated message.</param>
    /// <param name="cancellationToken">Application shutdown token.</param>
    /// <returns>False only when the database stayed unavailable and the message must be redelivered.</returns>
    private async Task<bool> TryWriteWithRetryAsync(
        Guid deviceRowId,
        string deviceId,
        ValidatedDeviceStatus status,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= _options.DbRetryCount; attempt++)
        {
            try
            {
                await _writer.WriteAsync(deviceRowId, status, cancellationToken);
                return true;
            }
            catch (PostgresException exception) when (exception.SqlState.StartsWith("23", StringComparison.Ordinal))
            {
                _logger.LogError(
                    "Device {DeviceId}: status violates constraint {ConstraintName} ({SqlState}) — dropping as poison",
                    deviceId,
                    exception.ConstraintName,
                    exception.SqlState);
                return true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Device {DeviceId}: status write attempt {Attempt}/{MaxAttempts} failed",
                    deviceId,
                    attempt,
                    _options.DbRetryCount);
                if (attempt == _options.DbRetryCount)
                {
                    break;
                }

                TimeSpan delay = TimeSpan.FromSeconds(
                    _options.DbRetryBaseDelaySeconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.LogError(
            "Device {DeviceId}: database unavailable after {MaxAttempts} attempts — requesting redelivery of a status message",
            deviceId,
            _options.DbRetryCount);
        return false;
    }

    /// <summary>One line per stored status, at a level that matches how much it matters.</summary>
    /// <param name="deviceId">MQTT device id.</param>
    /// <param name="status">What was stored.</param>
    private void LogStored(string deviceId, ValidatedDeviceStatus status)
    {
        if (status.Event is null)
        {
            _logger.LogDebug("Device {DeviceId}: online", deviceId);
            return;
        }

        LogLevel level = string.Equals(status.Event.Severity, DeviceEventSeverityNames.Normal, StringComparison.Ordinal)
            ? LogLevel.Debug
            : LogLevel.Information;

        _logger.Log(
            level,
            "Device {DeviceId}: {Kind} ({Reason}, {Severity})",
            deviceId,
            status.Event.Kind,
            status.Event.Reason,
            status.Event.Severity);
    }
}
