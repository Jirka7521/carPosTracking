namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Persists one validated status message: the event row, if it records one, and the
/// device's last-online time, if it says the device is online.
/// </summary>
internal interface IDeviceStatusWriter
{
    /// <summary>Writes one status message in a single transaction.</summary>
    /// <param name="deviceRowId">Database id of the device row.</param>
    /// <param name="status">The validated, classified message.</param>
    /// <param name="cancellationToken">Application shutdown token.</param>
    /// <returns>Completes when committed; throws on a database failure.</returns>
    Task WriteAsync(Guid deviceRowId, ValidatedDeviceStatus status, CancellationToken cancellationToken);
}
