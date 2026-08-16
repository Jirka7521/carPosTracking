namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Persists validated fixes for one device idempotently and stamps the device's
/// last-seen time.
/// </summary>
internal interface IPositionWriter
{
    /// <summary>Writes a batch of validated fixes for a single device.</summary>
    /// <param name="deviceId">Database id of the owning device row.</param>
    /// <param name="positions">Validated fixes from one MQTT message.</param>
    /// <param name="cancellationToken">Cancels the database round trips.</param>
    /// <returns>Inserted/duplicate counts.</returns>
    Task<PositionWriteResult> WriteBatchAsync(
        Guid deviceId,
        IReadOnlyList<ValidatedPosition> positions,
        CancellationToken cancellationToken);
}
