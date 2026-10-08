namespace CarPosAPI.Data.Entities;

/// <summary>
/// One immutable revision of a device's remote settings.
///
/// <para>
/// <b>Rows are never updated.</b> Changing a setting inserts a new row with the next
/// <see cref="Version"/> and repoints <see cref="Device.ConfigVersion"/> at it. That
/// is what makes the dashboard's pending view possible: a device reports back which
/// version it is actually running (<see cref="Device.ConfigAppliedVersion"/>), and
/// because the older row is still here, the UI can show the <em>values</em> in force
/// on the device beside the ones waiting to be picked up — not just two numbers.
/// The audit trail (who changed what, when) comes free with the same shape.
/// </para>
///
/// <para>
/// The values are the document published to <c>devices/&lt;id&gt;/config</c> and
/// cached by the firmware on its SD card; the bounds are
/// <see cref="Dtos.DeviceConfigRules"/>. Mapped by
/// <see cref="Configurations.DeviceConfigVersionConfiguration"/>.
/// </para>
/// </summary>
public sealed class DeviceConfigVersion
{
    /// <summary>Internal primary key. DB-generated.</summary>
    public Guid Id { get; set; }

    /// <summary>The device this revision belongs to (FK to <c>devices.id</c>).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// Revision number, unique and strictly increasing <em>per device</em> starting at
    /// <see cref="Dtos.DeviceConfigRules.InitialVersion"/>. Travels to the device in
    /// the config document and comes back in every position report, which is the whole
    /// synchronisation mechanism.
    /// </summary>
    public int Version { get; set; }

    /// <summary>Seconds between position reports.</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>Whether the device powers the modem down and deep-sleeps between reports.</summary>
    public bool SleepBetween { get; set; }

    /// <summary>How long the device chases a GNSS lock before giving up on a cycle, in seconds.</summary>
    public int FixTimeoutSeconds { get; set; }

    /// <summary>
    /// How many undelivered fixes the SD queue may hold before the oldest are dropped.
    /// A count rather than a duration because a queued line is bare ciphertext with no
    /// timestamp to age it by — see the firmware's <c>FixQueue</c>.
    /// </summary>
    public int QueueMaxFixes { get; set; }

    /// <summary>Hours between attempts on a fix this API rejected.</summary>
    public int RetryIntervalHours { get; set; }

    /// <summary>Hours after which a still-rejected fix is abandoned; 0 means never.</summary>
    public int RetryMaxAgeHours { get; set; }

    /// <summary>
    /// How often an <em>awake</em> device asks the broker to re-send this document,
    /// in seconds. Only a backstop: a saved change normally reaches the device by
    /// push within a second, and a device that reconnects (or wakes from deep
    /// sleep) is handed the retained document automatically. A deep-sleeping device
    /// ignores this entirely — it has no connection to check on.
    /// </summary>
    public int ConfigCheckSeconds { get; set; }

    // The motion block. The seven values above are the STANDBY set — what the device
    // runs while parked — and the Moving* values below are a full second copy of the
    // same seven for while it is driving. Everything here travels in the document's
    // nested "motion" object; see Dtos.DeviceMotionDocumentDto.

    /// <summary>
    /// Whether motion wake is on. Off by default: it changes how the device sleeps
    /// (an accelerometer wake source, and the Moving* set while driving), so it is
    /// switched on deliberately. While off, every other motion value is carried but
    /// inert.
    /// </summary>
    public bool MotionEnabled { get; set; }

    /// <summary>
    /// Accelerometer wake threshold, in milli-g. The ADXL345 compares in 62.5 mg steps
    /// and the firmware rounds this to the nearest one (63 mg = step 1, the most
    /// sensitive).
    /// </summary>
    public int MotionThresholdMg { get; set; }

    /// <summary>
    /// A fix counts as moving when its GNSS speed is strictly above this, in km/h. Not
    /// 0: a parked receiver reports 0-3 km/h of jitter.
    /// </summary>
    public int MotionSpeedKmph { get; set; }

    /// <summary>How long a wake may look for a moving fix before going back to sleep, in seconds.</summary>
    public int MotionWakeWaitSeconds { get; set; }

    /// <summary>How long after the last moving fix the device stays in moving mode, in seconds.</summary>
    public int MotionStopWaitSeconds { get; set; }

    /// <summary>Seconds between position reports while moving.</summary>
    public int MovingIntervalSeconds { get; set; }

    /// <summary>Whether the device deep-sleeps between reports while moving.</summary>
    public bool MovingSleepBetween { get; set; }

    /// <summary>How long the device chases a GNSS lock while moving, in seconds.</summary>
    public int MovingFixTimeoutSeconds { get; set; }

    /// <summary>
    /// Undelivered-fix queue cap while moving. The cap in force on the device is the
    /// LARGER of this and <see cref="QueueMaxFixes"/>, so a mode switch never trims
    /// fixes the other mode was still allowed to hold.
    /// </summary>
    public int MovingQueueMaxFixes { get; set; }

    /// <summary>Hours between attempts on a fix this API rejected, while moving.</summary>
    public int MovingRetryIntervalHours { get; set; }

    /// <summary>
    /// Hours after which a still-rejected fix is abandoned, while moving; 0 means
    /// never. The age in force on the device is the more lenient of this and
    /// <see cref="RetryMaxAgeHours"/> (0 beats any number).
    /// </summary>
    public int MovingRetryMaxAgeHours { get; set; }

    /// <summary>How often an awake device re-asks the broker for this document while moving, in seconds.</summary>
    public int MovingConfigCheckSeconds { get; set; }

    /// <summary>
    /// Who saved this revision. Null for the row seeded by the migration and for rows
    /// created alongside a device, neither of which has a human author to name.
    /// </summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>When this revision was saved (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// What produced this revision — a person, or the scheduler. See
    /// <see cref="ConfigRevisionSource"/> for why an authorless row is not enough to
    /// tell those apart.
    /// </summary>
    public ConfigRevisionSource Source { get; set; } = ConfigRevisionSource.Manual;

    /// <summary>
    /// The profile whose values this revision carries, when
    /// <see cref="Source"/> is <see cref="ConfigRevisionSource.Schedule"/>; null
    /// otherwise.
    ///
    /// <para>
    /// Nulled rather than cascaded when the profile is deleted, and the history keeps
    /// the values regardless: this names <em>where the numbers came from</em>, not
    /// where to look them up. The row is still a complete, self-contained record of
    /// what the device was told even after the profile behind it is gone.
    /// </para>
    /// </summary>
    public Guid? SourceProfileId { get; set; }
}
