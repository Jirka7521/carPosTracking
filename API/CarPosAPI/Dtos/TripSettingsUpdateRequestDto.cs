using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>PUT /api/devices/{deviceId}/trip-settings</c>. Saving different
/// values recomputes every trip of the device; journeys, names and manual placements
/// are kept wherever the trips are still recognisably the same.
/// </summary>
/// <param name="MinStopSeconds">Shortest stop that ends a trip.</param>
/// <param name="StopRadiusMeters">How far the car may wander while still counting as stopped.</param>
/// <param name="MinTripDistanceMeters">Movements shorter than this are not trips.</param>
public sealed record TripSettingsUpdateRequestDto(
    [Range(TripRules.MinStopSecondsLowerBound, TripRules.MinStopSecondsUpperBound)]
    int MinStopSeconds,
    [Range(TripRules.MinStopRadiusMeters, TripRules.MaxStopRadiusMeters)]
    int StopRadiusMeters,
    [Range(TripRules.MinTripDistanceLowerBound, TripRules.MinTripDistanceUpperBound)]
    int MinTripDistanceMeters);
