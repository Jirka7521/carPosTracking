namespace CarPosAPI.Dtos;

/// <summary>
/// The bounds and constants of the Trips &amp; Journeys feature: detection thresholds,
/// journey limits, and the fixed parameters of the stop-finding algorithm.
///
/// <para>
/// The sibling of <see cref="ScheduleRules"/> and <see cref="DeviceConfigRules"/>, and
/// here for the same reason: the user-tunable bounds feed the <c>[Range]</c>
/// attributes on the request DTOs, the CHECK constraints in the trip configurations
/// and the <c>min</c>/<c>max</c> on the dashboard's inputs. One definition, three
/// enforcement points.
/// </para>
///
/// <para>
/// <b>Where the fixed values come from.</b> <c>docs/MOTION-WAKE-THRESHOLDS.md</c>
/// analysed a real car's history: a moving device reports every 30 s (median gap
/// 66 s), outages of more than 5 min are "unknown" rather than "parked", and
/// "moving" is best read as above 5 km/h. The firmware stays awake for
/// <c>motion_stop_wait_s</c> (15 min by default) after the car stops, then sleeps and
/// reports every 20 min — so a parked car keeps producing fixes from the same spot,
/// which is why stops are found by place (stay-points), never by gaps alone.
/// </para>
/// </summary>
public static class TripRules
{
    /// <summary>Shortest stop that may be configured to end a trip: one minute.</summary>
    public const int MinStopSecondsLowerBound = 60;

    /// <summary>Longest stop that may be configured to end a trip: four hours.</summary>
    public const int MinStopSecondsUpperBound = 14_400;

    /// <summary>
    /// Default minimum stop that ends a trip: 20 minutes. Chosen by the product owner;
    /// a shop visit or refuelling stays inside one trip.
    /// </summary>
    public const int DefaultMinStopSeconds = 1_200;

    /// <summary>Smallest stop radius — below GNSS jitter a parked car would never "stop".</summary>
    public const int MinStopRadiusMeters = 20;

    /// <summary>Largest stop radius — beyond this, slow city driving would read as a stop.</summary>
    public const int MaxStopRadiusMeters = 1_000;

    /// <summary>Default stop radius: comfortably above parked-car jitter, below a city block.</summary>
    public const int DefaultStopRadiusMeters = 150;

    /// <summary>Shortest configurable minimum trip distance (0 = keep every movement).</summary>
    public const int MinTripDistanceLowerBound = 0;

    /// <summary>Longest configurable minimum trip distance: 20 km.</summary>
    public const int MinTripDistanceUpperBound = 20_000;

    /// <summary>
    /// Default minimum trip distance: 500 m, so re-parking or GNSS drift inside a car
    /// park does not become a trip.
    /// </summary>
    public const int DefaultMinTripDistanceMeters = 500;

    /// <summary>
    /// Above this speed a leg counts towards a trip's moving time. From the threshold
    /// analysis (moving = above 5 km/h).
    /// </summary>
    public const double MovingSpeedKmph = 5.0;

    /// <summary>
    /// A leg between two consecutive fixes longer than this is a data gap. A moving
    /// device reports every 30 s, so five minutes of silence means it was asleep or had
    /// no GNSS fix — the analysis' threshold for an outage.
    /// </summary>
    public const int DataGapSeconds = 300;

    /// <summary>
    /// How long the car must already have stood still before a data gap for that gap to
    /// count as parked time. The firmware only sleeps after standing still for
    /// <c>motion_stop_wait_s</c>, so silence after a still spell means "asleep, parked".
    /// </summary>
    public const int StillBeforeSleepSeconds = 300;

    /// <summary>
    /// A data gap at least this long across which the car moved further than the stop
    /// radius ends the trip: what happened in between is unknown, and bridging hours of
    /// silence with a straight line would invent a drive.
    /// </summary>
    public const int MaxBridgedGapSeconds = 7_200;

    /// <summary>Longest journey or trip name.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Deepest journey nesting: a top-level journey is level 1.</summary>
    public const int MaxJourneyDepth = 5;

    /// <summary>How many journeys one car may have.</summary>
    public const int MaxJourneysPerDevice = 10_000;

    /// <summary>Trips per list page when the caller does not say.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Largest trips page a caller may ask for.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Most trips one assign request may move.</summary>
    public const int MaxTripsPerAssign = 500;

    /// <summary>
    /// Smallest location-rule radius. A trip's start and end are fixes that can sit up to
    /// the stop radius from where the car actually parked, plus GNSS jitter, so a tighter
    /// circle would miss trips that plainly ended "there".
    /// </summary>
    public const int MinLocationRuleRadiusMeters = 50;

    /// <summary>Largest location-rule radius: 10 km, a whole small town.</summary>
    public const int MaxLocationRuleRadiusMeters = 10_000;

    /// <summary>Radius a new location rule starts with: about one car park and its street.</summary>
    public const int DefaultLocationRuleRadiusMeters = 200;

    /// <summary>
    /// Most rules one car may have in force at once. Every unpinned trip is tested against
    /// the rules valid at its start, so this bounds the work of every trip read.
    /// </summary>
    public const int MaxActiveLocationRulesPerDevice = 100;

    /// <summary>Most rules one car may have in all, ended ones (the history) included.</summary>
    public const int MaxLocationRulesPerDevice = 1_000;

    /// <summary>Most matched trip points a location-rule preview returns for the map.</summary>
    public const int MaxLocationRulePreviewPoints = 500;
}
