namespace CarPosAPI.Services.Trips;

/// <summary>
/// Great-circle distance on a spherical Earth (haversine). Accurate to a few tenths of a
/// percent against the ellipsoid — far inside GNSS error at trip scale — and free of any
/// spatial dependency, like the rest of the application.
/// </summary>
internal static class GeoDistance
{
    // IUGG mean Earth radius.
    private const double EarthRadiusMeters = 6_371_008.8;

    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Distance in metres between two points given in decimal degrees.</summary>
    /// <param name="latitude1">Latitude of the first point.</param>
    /// <param name="longitude1">Longitude of the first point.</param>
    /// <param name="latitude2">Latitude of the second point.</param>
    /// <param name="longitude2">Longitude of the second point.</param>
    /// <returns>The great-circle distance in metres.</returns>
    public static double Meters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        double phi1 = latitude1 * DegreesToRadians;
        double phi2 = latitude2 * DegreesToRadians;
        double deltaPhi = (latitude2 - latitude1) * DegreesToRadians;
        double deltaLambda = (longitude2 - longitude1) * DegreesToRadians;

        double sinHalfPhi = Math.Sin(deltaPhi / 2.0);
        double sinHalfLambda = Math.Sin(deltaLambda / 2.0);
        double a = (sinHalfPhi * sinHalfPhi) + (Math.Cos(phi1) * Math.Cos(phi2) * sinHalfLambda * sinHalfLambda);

        // Min guards the rounding that can push a just past 1 for antipodal points.
        return 2.0 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Distance in metres between two fixes.</summary>
    /// <param name="from">First fix.</param>
    /// <param name="to">Second fix.</param>
    /// <returns>The great-circle distance in metres.</returns>
    public static double Meters(TripPoint from, TripPoint to) =>
        Meters(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
}
