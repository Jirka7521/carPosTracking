using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Reads a device's trips for the dashboard, rebuilding each trip's end and statistics
/// from the positions on the way out. Every method authorises the device first.
/// </summary>
public interface ITripReadService
{
    /// <summary>One page of trips, newest first.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journey"><c>all</c> (or null), <c>none</c>, or a journey id: trips directly in it.</param>
    /// <param name="before">The previous page's cursor, or null for the first page.</param>
    /// <param name="limit">Page size (already clamped).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, or why not.</returns>
    Task<OperationResult<TripPageDto>> ListAsync(
        int userId,
        string deviceId,
        string? journey,
        DateTime? before,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>One trip.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="tripId">The trip.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The trip, or why not.</returns>
    Task<OperationResult<TripSummaryDto>> GetAsync(int userId, string deviceId, long tripId, CancellationToken cancellationToken);

    /// <summary>What a date range holds — for the journey dialog's preview.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="from">Inclusive start (UTC).</param>
    /// <param name="to">Exclusive end (UTC).</param>
    /// <param name="journeyId">The journey being edited, if any (its own pinned trips are not "elsewhere").</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The summary, or why not.</returns>
    Task<OperationResult<TripRangeSummaryDto>> SummarizeRangeAsync(
        int userId,
        string deviceId,
        DateTime from,
        DateTime to,
        Guid? journeyId,
        CancellationToken cancellationToken);

    /// <summary>A journey's totals, including its sub-journeys.</summary>
    /// <param name="userId">The caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="journeyId">The journey.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The totals, or why not.</returns>
    Task<OperationResult<JourneySummaryDto>> SummarizeJourneyAsync(
        int userId,
        string deviceId,
        Guid journeyId,
        CancellationToken cancellationToken);
}
