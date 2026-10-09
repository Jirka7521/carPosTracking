namespace CarPosAPI.Dtos;

/// <summary>
/// The accepted range and the factory default for every remote device setting.
///
/// <para>
/// These numbers are a <b>mirror of the firmware's</b> <c>ESP32/src/config/Config.h</c>
/// (<c>kMin*</c>/<c>kMax*</c> and the <c>kDefault*</c>/subsystem constants beside
/// them). The two sides enforce them differently on purpose: the device
/// <em>clamps</em> an out-of-range value, because a tracker in a field must keep
/// reporting whatever nonsense it is handed, while this API <em>rejects</em> it with
/// a 400, because a dashboard user can be told to fix their input. Same numbers,
/// opposite failure modes — change one side and you must change the other.
/// </para>
///
/// <para>
/// They live in <c>Dtos/</c> rather than behind the service layer because they are
/// part of the published contract: the same constants feed the <c>[Range]</c>
/// attributes on <see cref="UpdateDeviceConfigRequestDto"/>, the CHECK constraints
/// in <c>DeviceConfigVersionConfiguration</c>, and the <c>min</c>/<c>max</c> on the
/// dashboard's number inputs. <c>const</c>, not <c>static readonly</c>, because
/// DataAnnotations arguments must be compile-time constants.
/// </para>
/// </summary>
public static class DeviceConfigRules
{
    /// <summary>Minimum seconds between position reports.</summary>
    public const int MinIntervalSeconds = 5;

    /// <summary>Maximum seconds between position reports (24 h).</summary>
    public const int MaxIntervalSeconds = 86400;

    /// <summary>
    /// Factory default reporting cadence, in seconds (20 min). This is the STANDBY
    /// interval — motion wake is on by default — so it is how often a parked vehicle
    /// reports: a heartbeat, and the timer safety net for a trip the accelerometer
    /// missed. The moving set's <see cref="DefaultMovingIntervalSeconds"/> takes over
    /// once the vehicle is driving.
    /// </summary>
    public const int DefaultIntervalSeconds = 1200;

    /// <summary>Factory default for deep-sleeping between reports.</summary>
    public const bool DefaultSleepBetween = false;

    /// <summary>Minimum GNSS acquire budget — below the modem's poll step it could never succeed.</summary>
    public const int MinFixTimeoutSeconds = 15;

    /// <summary>Maximum GNSS acquire budget (1 h), so a bad config cannot hold a sleeping device awake.</summary>
    public const int MaxFixTimeoutSeconds = 3600;

    /// <summary>Factory default GNSS acquire budget, in seconds.</summary>
    public const int DefaultFixTimeoutSeconds = 180;

    /// <summary>Minimum size of the undelivered-fix queue on the SD card.</summary>
    public const int MinQueueMaxFixes = 100;

    /// <summary>
    /// Maximum size of the undelivered-fix queue on the SD card. A policy bound on
    /// card usage rather than a technical one: at ~1 KB per sealed envelope this is
    /// roughly 100 MB, which is months of backlog at any sane reporting interval.
    /// </summary>
    public const int MaxQueueMaxFixes = 100000;

    /// <summary>Factory default size of the undelivered-fix queue.</summary>
    public const int DefaultQueueMaxFixes = 20000;

    /// <summary>Minimum hours between attempts on a rejected fix.</summary>
    public const int MinRetryIntervalHours = 1;

    /// <summary>Maximum hours between attempts on a rejected fix (30 days).</summary>
    public const int MaxRetryIntervalHours = 720;

    /// <summary>Factory default retry pacing, in hours.</summary>
    public const int DefaultRetryIntervalHours = 24;

    /// <summary>
    /// Minimum give-up age for a rejected fix. Zero is meaningful here — it is the
    /// "never give up" value — which is why this floor is 0 and not 1.
    /// </summary>
    public const int MinRetryMaxAgeHours = 0;

    /// <summary>Maximum give-up age for a rejected fix (one year).</summary>
    public const int MaxRetryMaxAgeHours = 8760;

    /// <summary>Factory default give-up age, in hours (7 days).</summary>
    public const int DefaultRetryMaxAgeHours = 168;

    /// <summary>
    /// Minimum interval for the device's periodic configuration re-check. The
    /// one-minute floor is deliberate: a change normally reaches a device by push
    /// within a second, so this is only a backstop against a connection that looks
    /// alive but delivers nothing. Allowing a few seconds here would invite a
    /// configuration that wakes the tracker constantly for no benefit.
    /// </summary>
    public const int MinConfigCheckSeconds = 60;

    /// <summary>Maximum interval for the periodic configuration re-check (24 h).</summary>
    public const int MaxConfigCheckSeconds = 86400;

    /// <summary>
    /// Factory default configuration re-check interval, in seconds (1 h).
    ///
    /// <para>
    /// An hour rather than something eager, because this only paces the
    /// <em>backstop</em>. A real change reaches an awake device by push within a
    /// second, and a device that reconnects or wakes from deep sleep is handed the
    /// retained document automatically — so all a shorter interval buys is faster
    /// recovery from a connection that looks alive but delivers nothing. The
    /// firmware cuts its idle wait into chunks of at most this value, so the number
    /// also sets how often every awake tracker wakes and puts a SUBSCRIBE on the
    /// wire. Cheap per event, but paid by every device for ever.
    /// </para>
    /// </summary>
    public const int DefaultConfigCheckSeconds = 3600;

    /// <summary>
    /// Factory default for motion wake. On: reporting every 30 s while driving and
    /// every 20 min while parked is what a freshly provisioned tracker is for. It can be
    /// switched off from the dashboard, which leaves the standby set as the only one.
    /// </summary>
    public const bool DefaultMotionEnabled = true;

    /// <summary>
    /// Minimum wake threshold, in milli-g. The ADXL345 compares in steps of 62.5 mg
    /// and the firmware rounds the value to the nearest step, so 63 mg is step 1 —
    /// the most sensitive setting the sensor offers. The floor is that one step
    /// because a threshold of 0 is the value the datasheet warns misbehaves.
    /// </summary>
    public const int MinMotionThresholdMg = 63;

    /// <summary>
    /// Maximum wake threshold, in milli-g: the sensor's +/-2 g range, beyond which a
    /// reading cannot change by much more and the device would simply never wake.
    /// </summary>
    public const int MaxMotionThresholdMg = 2000;

    /// <summary>
    /// Factory default wake threshold, in milli-g: step 3 (187.5 mg), the step nearest
    /// 0.16 g. Stored as 188 so the number is what the sensor actually uses rather than
    /// a value the firmware silently rounds. Less eager than step 1 — fewer false wakes
    /// (<c>docs/MOTION-WAKE-THRESHOLDS.md</c> measured 2.1 a day against 3.1) for a
    /// slightly later first catch.
    /// </summary>
    public const int DefaultMotionThresholdMg = 188;

    /// <summary>
    /// Minimum speed, in km/h, above which a fix counts as moving. Never 0: a parked
    /// GNSS receiver reports 0-3 km/h of jitter, so a floor of 0 would let a stationary
    /// car look as if it were driving and keep the device awake for ever.
    /// </summary>
    public const int MinMotionSpeedKmph = 1;

    /// <summary>Maximum speed, in km/h, that still counts as moving — a policy bound, far above any crawl.</summary>
    public const int MaxMotionSpeedKmph = 50;

    /// <summary>
    /// Factory default moving speed, in km/h. A fix counts as moving when its speed is
    /// <em>strictly above</em> this, which clears the receiver's parked jitter.
    /// </summary>
    public const int DefaultMotionSpeedKmph = 3;

    /// <summary>Minimum seconds a wake may look for a moving fix before going back to sleep.</summary>
    public const int MinMotionWakeWaitSeconds = 30;

    /// <summary>Maximum seconds a wake may look for a moving fix (1 h).</summary>
    public const int MaxMotionWakeWaitSeconds = 3600;

    /// <summary>
    /// Factory default wake wait, in seconds (10 min). Long enough to cover a cold GNSS
    /// start and a driver who gets in and pulls away several minutes later.
    /// </summary>
    public const int DefaultMotionWakeWaitSeconds = 600;

    /// <summary>
    /// Minimum seconds the device stays in moving mode after the last moving fix. Mirrors
    /// the firmware's floor; below a minute even one red light could end a trip.
    /// </summary>
    public const int MinMotionStopWaitSeconds = 60;

    /// <summary>Maximum seconds the device stays in moving mode after the last moving fix (2 h).</summary>
    public const int MaxMotionStopWaitSeconds = 7200;

    /// <summary>
    /// Factory default stop wait, in seconds (15 min). Rides out traffic stops and a
    /// quick errand without ending the trip; shorter costs a fresh wake at more stops,
    /// longer only burns awake time.
    /// </summary>
    public const int DefaultMotionStopWaitSeconds = 900;

    // The MOVING set: interval, sleep and fix timeout only. Its accepted ranges are
    // deliberately NOT repeated here: a setting means the same thing in either mode, so
    // a moving value is validated against the standby Min*/Max* constants above and
    // only its default differs.
    //
    // The queue cap, both retry settings and the config re-check have no moving copy.
    // They govern storage and the link rather than how the vehicle is sampled, and
    // letting the queue cap or give-up age follow a mode switch would throw data away
    // every time a car parked.

    /// <summary>
    /// Factory default reporting cadence while moving, in seconds. Far tighter than the
    /// standby 20 min, because a track is only worth having if the vehicle is sampled
    /// often enough to follow its turns.
    /// </summary>
    public const int DefaultMovingIntervalSeconds = 30;

    /// <summary>
    /// Factory default for deep-sleeping between reports while moving. Off: at a 30 s
    /// cadence a sleep/wake cycle costs a cold fix and a TLS handshake each time and
    /// saves almost nothing, and a moving vehicle is the one case where power is not
    /// the constraint.
    /// </summary>
    public const bool DefaultMovingSleepBetween = false;

    /// <summary>Factory default GNSS acquire budget while moving, in seconds.</summary>
    public const int DefaultMovingFixTimeoutSeconds = 180;

    /// <summary>
    /// Version number every device's first configuration row is created with.
    /// Versions are per device and strictly increasing; there is no version 0.
    /// </summary>
    public const int InitialVersion = 1;
}
