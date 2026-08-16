using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Positions;

/// <summary>
/// Reads stored GNSS fixes back out for the map and the position table. Read-only
/// by design — positions are written exclusively by the ingest pipeline
/// (<see cref="Ingest.PositionWriter"/>), and nothing in the HTTP surface may
/// create, edit or delete one.
/// </summary>
public interface IPositionQueryService
{
    /// <summary>Loads one device's fixes, newest first.</summary>
    /// <param name="userId">The authenticated caller; must hold a grant on the device.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="fromUtc">Inclusive lower bound on fix time, or null for no lower bound.</param>
    /// <param name="toUtc">Inclusive upper bound on fix time, or null for no upper bound.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// Up to a fixed maximum of fixes, or the reason the caller may not have them.
    /// The cap is not negotiable by the client: <c>positions</c> grows without
    /// bound, and an unbounded read would eventually take the API down.
    /// </returns>
    Task<OperationResult<IReadOnlyList<PositionDto>>> ListForDeviceAsync(
        int userId,
        string deviceId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken);
}
