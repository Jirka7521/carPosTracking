namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Processes one message from a device's status topic end to end: device lookup →
/// envelope decode → decrypt → validate and classify → write. The status-topic
/// counterpart of <see cref="IIngestPipeline"/>, with the same acknowledge contract.
/// </summary>
internal interface IStatusIngestPipeline
{
    /// <summary>Processes one status message.</summary>
    /// <param name="deviceId">Device id already parsed from <c>devices/&lt;id&gt;/status</c>.</param>
    /// <param name="payload">Raw payload bytes.</param>
    /// <param name="cancellationToken">Application shutdown token.</param>
    /// <returns>Whether the message may be acknowledged.</returns>
    Task<IngestOutcome> ProcessAsync(string deviceId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}
