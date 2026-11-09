namespace CarPosAPI.Dtos;

/// <summary>Result of <c>POST /api/devices/{deviceId}/trips/assign</c>.</summary>
/// <param name="Moved">
/// How many trips were updated. Fewer than requested when some no longer exist — a
/// recompute may merge trips between the list being shown and the click.
/// </param>
public sealed record TripAssignResultDto(int Moved);
