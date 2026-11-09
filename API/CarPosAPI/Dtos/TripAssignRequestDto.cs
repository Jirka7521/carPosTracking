using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST /api/devices/{deviceId}/trips/assign</c>: places trips by hand,
/// or hands them back to the journeys' date ranges.
/// </summary>
/// <param name="TripIds">The trips to move; ids not belonging to the device are ignored.</param>
/// <param name="JourneyId">
/// The journey to pin them to, or null to pin them to "no journey". Ignored when
/// <paramref name="Automatic"/> is true.
/// </param>
/// <param name="Automatic">
/// True to unpin the trips, so they follow the journeys' date ranges again.
/// </param>
public sealed record TripAssignRequestDto(
    [Required]
    [MinLength(1)]
    [MaxLength(TripRules.MaxTripsPerAssign)]
    long[] TripIds,
    Guid? JourneyId,
    bool Automatic);
