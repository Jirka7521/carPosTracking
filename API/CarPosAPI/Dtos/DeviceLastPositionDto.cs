namespace CarPosAPI.Dtos;

/// <summary>
/// Where a device's most recent fix was, so the dashboard can say at a glance where the
/// vehicle is (the address is looked up in the viewer's browser, after their consent to
/// Google Maps — never by this server). Every active grant can read the device's
/// positions anyway, so this exposes nothing new.
/// </summary>
/// <param name="Latitude">Latitude of the fix, decimal degrees.</param>
/// <param name="Longitude">Longitude of the fix, decimal degrees.</param>
/// <param name="FixTime">When the fix was taken (UTC).</param>
public sealed record DeviceLastPositionDto(
    double Latitude,
    double Longitude,
    DateTime FixTime);
