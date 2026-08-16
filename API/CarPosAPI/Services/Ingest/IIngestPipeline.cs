namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Processes one raw MQTT message end to end: topic guard → device lookup →
/// envelope decode → decrypt → validate → idempotent batch insert.
/// </summary>
internal interface IIngestPipeline
{
    /// <summary>Processes one message.</summary>
    /// <param name="topic">The topic the message arrived on.</param>
    /// <param name="payload">Raw payload bytes.</param>
    /// <param name="cancellationToken">Application shutdown token.</param>
    /// <returns>Whether the message may be acknowledged.</returns>
    Task<IngestOutcome> ProcessAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}
