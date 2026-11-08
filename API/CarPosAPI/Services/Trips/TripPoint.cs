namespace CarPosAPI.Services.Trips;

/// <summary>
/// One fix as the trip logic needs it: when, where and how fast.
///
/// <para>
/// A separate type from <see cref="Data.Entities.Position"/> for the same reason
/// <c>ScheduleRuleSnapshot</c> exists: the detector and analyzer stay free of EF Core,
/// and the tests build tracks from plain records with no database.
/// </para>
/// </summary>
/// <param name="FixTime">GNSS fix time (UTC).</param>
/// <param name="Latitude">Decimal degrees, +N/−S.</param>
/// <param name="Longitude">Decimal degrees, +E/−W.</param>
/// <param name="SpeedKmph">Receiver ground speed in km/h.</param>
internal sealed record TripPoint(DateTime FixTime, double Latitude, double Longitude, double SpeedKmph);
