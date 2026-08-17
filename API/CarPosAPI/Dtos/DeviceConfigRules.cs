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

    /// <summary>Factory default reporting cadence, in seconds.</summary>
    public const int DefaultIntervalSeconds = 60;

    /// <summary>Factory default for deep-sleeping between reports.</summary>
    public const bool DefaultSleepBetween = false;

    /// <summary>Minimum GNSS acquire budget — below the modem's poll step it could never succeed.</summary>
    public const int MinFixTimeoutSeconds = 15;

    /// <summary>Maximum GNSS acquire budget (15 min), so a bad config cannot hold a sleeping device awake.</summary>
    public const int MaxFixTimeoutSeconds = 900;

    /// <summary>Factory default GNSS acquire budget, in seconds.</summary>
    public const int DefaultFixTimeoutSeconds = 180;

    /// <summary>Minimum size of the undelivered-fix queue on the SD card.</summary>
    public const int MinQueueMaxFixes = 100;

    /// <summary>Maximum size of the undelivered-fix queue on the SD card.</summary>
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

    /// <summary>Factory default configuration re-check interval, in seconds (15 min).</summary>
    public const int DefaultConfigCheckSeconds = 900;

    /// <summary>
    /// Version number every device's first configuration row is created with.
    /// Versions are per device and strictly increasing; there is no version 0.
    /// </summary>
    public const int InitialVersion = 1;
}
