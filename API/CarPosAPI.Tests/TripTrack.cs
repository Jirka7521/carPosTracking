using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// Builds synthetic GNSS tracks for the trip tests, so each scenario reads as what the
/// car and the firmware did — "parked 30 min, drove 10 km at 60 km/h, slept" — instead
/// of a wall of coordinates.
///
/// <para>
/// Mirrors the firmware's real cadence by default: a fix every 30 s while awake. The car
/// drives due east from 50° N 14° E (near Prague); one metre east is converted to degrees
/// of longitude at the current latitude.
/// </para>
/// </summary>
internal sealed class TripTrack
{
    /// <summary>Awake reporting interval of the firmware (seconds).</summary>
    public const int AwakeIntervalSeconds = 30;

    private const double MetersPerDegreeLatitude = 111_195.0;

    private readonly List<TripPoint> _points = new List<TripPoint>();
    private DateTime _time;
    private double _latitude = 50.0;
    private double _longitude = 14.0;

    /// <summary>Starts a track at <paramref name="startUtc"/>.</summary>
    /// <param name="startUtc">Time of the first fix.</param>
    public TripTrack(DateTime startUtc)
    {
        _time = startUtc;
    }

    /// <summary>The fixes so far.</summary>
    public IReadOnlyList<TripPoint> Points => _points;

    /// <summary>The current clock of the track (time of the next fix).</summary>
    public DateTime Now => _time;

    /// <summary>Stands still for <paramref name="duration"/>, reporting every <paramref name="intervalSeconds"/>.</summary>
    /// <param name="duration">How long the car stands.</param>
    /// <param name="intervalSeconds">Reporting interval.</param>
    /// <param name="jitterMeters">Alternating north/south offset per fix, to mimic GNSS noise.</param>
    /// <returns>This track.</returns>
    public TripTrack Park(TimeSpan duration, int intervalSeconds = AwakeIntervalSeconds, double jitterMeters = 0)
    {
        DateTime until = _time + duration;
        int fix = 0;
        while (_time < until)
        {
            double offset = fix % 2 == 0 ? jitterMeters : -jitterMeters;
            Emit(_latitude + (offset / MetersPerDegreeLatitude), _longitude, 0);
            _time = _time.AddSeconds(intervalSeconds);
            fix++;
        }

        return this;
    }

    /// <summary>Drives east for <paramref name="meters"/> at <paramref name="speedKmph"/>.</summary>
    /// <param name="meters">Distance to cover.</param>
    /// <param name="speedKmph">Constant speed.</param>
    /// <param name="intervalSeconds">Reporting interval.</param>
    /// <returns>This track.</returns>
    public TripTrack Drive(double meters, double speedKmph, int intervalSeconds = AwakeIntervalSeconds)
    {
        double metersPerFix = speedKmph / 3.6 * intervalSeconds;
        double covered = 0;
        while (covered < meters)
        {
            double step = Math.Min(metersPerFix, meters - covered);
            MoveEast(step);
            covered += step;
            _time = _time.AddSeconds(intervalSeconds * (step / metersPerFix));
            Emit(_latitude, _longitude, speedKmph);
        }

        _time = _time.AddSeconds(intervalSeconds);
        return this;
    }

    /// <summary>Lets time pass with no fixes (device asleep, or no GNSS).</summary>
    /// <param name="duration">Length of the silence.</param>
    /// <returns>This track.</returns>
    public TripTrack Silence(TimeSpan duration)
    {
        _time += duration;
        return this;
    }

    /// <summary>Moves the car east without any fix being recorded (driving during a silence).</summary>
    /// <param name="meters">Distance covered unseen.</param>
    /// <returns>This track.</returns>
    public TripTrack MoveUnseen(double meters)
    {
        MoveEast(meters);
        return this;
    }

    /// <summary>Emits one fix at the current place and time.</summary>
    /// <param name="speedKmph">Reported speed.</param>
    /// <returns>This track.</returns>
    public TripTrack Fix(double speedKmph = 0)
    {
        Emit(_latitude, _longitude, speedKmph);
        _time = _time.AddSeconds(AwakeIntervalSeconds);
        return this;
    }

    private void MoveEast(double meters)
    {
        double metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(_latitude * Math.PI / 180.0);
        _longitude += meters / metersPerDegreeLongitude;
    }

    private void Emit(double latitude, double longitude, double speedKmph)
    {
        _points.Add(new TripPoint(_time, latitude, longitude, speedKmph));
    }
}
