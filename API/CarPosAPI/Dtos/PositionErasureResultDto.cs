namespace CarPosAPI.Dtos;

/// <summary>
/// Response of <c>DELETE /api/devices/{deviceId}/positions</c>: how many stored
/// fixes were destroyed. Returned rather than a bare 204 so the dashboard can say
/// "1 284 positions erased" instead of "done" — which is the difference between a
/// user believing an erasure happened and knowing it did.
/// </summary>
/// <param name="DeletedCount">Number of position rows deleted.</param>
public sealed record PositionErasureResultDto(long DeletedCount);
