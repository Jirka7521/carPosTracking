using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Positions;

/// <summary>
/// Deletes a device's stored position history — the narrow, per-device form of the
/// right to erasure (GDPR Art. 17).
///
/// This is the lever that stands in for an automatic retention job. Positions are
/// kept indefinitely by design (see docs/PRIVACY.md), so the only thing that
/// bounds a location history is somebody deciding it should end. That decision
/// belongs to whoever holds <c>CanDelete</c> on the device, and it has to be a
/// real delete: a flag on a row that still contains coordinates erases nothing.
/// </summary>
public interface IPositionErasureService
{
    /// <summary>Erases a device's positions, optionally only within a time range.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="fromUtc">Inclusive lower bound on fix time; null for "from the beginning".</param>
    /// <param name="toUtc">Inclusive upper bound on fix time; null for "to the end".</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// How many rows were deleted, <see cref="OperationOutcome.NotFound"/> when the
    /// device is not visible to the caller, or <see cref="OperationOutcome.Forbidden"/>
    /// when they can see it but may not delete from it.
    /// </returns>
    Task<OperationResult<long>> EraseAsync(
        int userId,
        string deviceId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken);
}
