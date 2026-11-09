namespace CarPosAPI.Dtos;

/// <summary>One page of <c>GET /api/devices/{deviceId}/trips</c>, newest first.</summary>
/// <param name="Items">The trips on this page.</param>
/// <param name="NextCursor">
/// Pass as <c>before</c> to fetch the next (older) page; null on the last page.
/// </param>
/// <param name="IsRecalculating">
/// True while the device's older trips are being recomputed — after a threshold change,
/// or while a late offline backlog is folded in. The list may shift when it finishes.
/// </param>
public sealed record TripPageDto(IReadOnlyList<TripSummaryDto> Items, DateTime? NextCursor, bool IsRecalculating);
