namespace CarPosAPI.Dtos;

/// <summary>Response of <c>GET /api/devices/{deviceId}/journeys</c>.</summary>
/// <param name="Journeys">Every journey of the device, flat, each with its parent id.</param>
/// <param name="AllTripCount">Every trip of the device.</param>
/// <param name="UnassignedTripCount">Trips in no journey.</param>
public sealed record JourneyListDto(IReadOnlyList<JourneyDto> Journeys, int AllTripCount, int UnassignedTripCount);
