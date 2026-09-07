namespace CarPosAPI.Services.Privacy;

/// <summary>
/// Produces the "everything you hold about me" document required by GDPR Art. 15
/// (access) and Art. 20 (portability).
///
/// It writes straight to a stream rather than returning an object because a
/// complete position history is the whole point: an account with a tracker running
/// for a year has hundreds of thousands of fixes, and materialising them into a
/// DTO graph just to serialise it would put the entire history in memory to hand
/// out a copy of it. The read cap that protects the dashboard
/// (<c>PositionQueryService.MaxPositionsPerQuery</c>) is deliberately not applied
/// here — a truncated export is not portability.
/// </summary>
public interface IDataExportService
{
    /// <summary>
    /// Writes the caller's complete personal-data export as JSON.
    /// </summary>
    /// <param name="userId">The account being exported. Always the caller's own.</param>
    /// <param name="destination">Stream to write the JSON document to.</param>
    /// <param name="cancellationToken">Cancels the database work and the write.</param>
    /// <returns>A task that completes when the document has been written and flushed.</returns>
    Task WriteExportAsync(int userId, Stream destination, CancellationToken cancellationToken);
}
