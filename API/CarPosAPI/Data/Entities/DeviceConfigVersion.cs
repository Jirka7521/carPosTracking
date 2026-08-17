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

    /// <summary>
    /// Who saved this revision. Null for the row seeded by the migration and for rows
    /// created alongside a device, neither of which has a human author to name.
    /// </summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>When this revision was saved (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }
}
