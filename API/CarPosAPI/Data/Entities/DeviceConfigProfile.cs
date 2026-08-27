namespace CarPosAPI.Data.Entities;

/// <summary>
/// A named, reusable set of the seven remote settings — "Night", "Weekend", "Commute".
///
/// <para>
/// A profile is <b>not</b> a revision. <see cref="DeviceConfigVersion"/> rows are
/// immutable history: what a device was told, and when. A profile is editable intent:
/// what the device should be told whenever a rule selects it. Editing a profile that
/// is currently in force makes the scheduler write a <em>new</em> revision, which is
/// how the two stay in their own lanes — the history never rewrites itself, and the
/// intent is never buried in it.
/// </para>
///
/// <para>
/// The values and their bounds are exactly those of a revision
/// (<see cref="Dtos.DeviceConfigRules"/>), duplicated as columns rather than shared
/// through a base type because EF Core would otherwise want a hierarchy here for no
/// benefit — the two tables have different lifecycles and different keys. The CHECK
/// constraints in <see cref="Configurations.DeviceConfigProfileConfiguration"/> are
/// built from the same constants, so the two can never drift.
/// </para>
///
/// <para>
/// Profiles are per device, like the revisions they produce. Mapped by
/// <see cref="Configurations.DeviceConfigProfileConfiguration"/>.
/// </para>
/// </summary>
public sealed class DeviceConfigProfile
{
    /// <summary>Internal primary key. DB-generated.</summary>
    public Guid Id { get; set; }

    /// <summary>The device this profile belongs to (FK to <c>devices.id</c>).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// What a person calls it. Unique per device, case-insensitively — two profiles
    /// called "Night" and "night" would make every rule list ambiguous to read, which
    /// defeats the point of naming them at all.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Seconds between position reports.</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>Whether the device powers the modem down and deep-sleeps between reports.</summary>
    public bool SleepBetween { get; set; }

    /// <summary>How long the device chases a GNSS lock before giving up on a cycle, in seconds.</summary>
    public int FixTimeoutSeconds { get; set; }

    /// <summary>How many undelivered fixes the SD queue may hold before the oldest are dropped.</summary>
    public int QueueMaxFixes { get; set; }

    /// <summary>Hours between attempts on a fix this API rejected.</summary>
    public int RetryIntervalHours { get; set; }

    /// <summary>Hours after which a still-rejected fix is abandoned; 0 means never.</summary>
    public int RetryMaxAgeHours { get; set; }

    /// <summary>How often an awake device asks the broker to re-send its configuration, in seconds.</summary>
    public int ConfigCheckSeconds { get; set; }

    /// <summary>Who created it. Null when the author's account has since been removed.</summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>When it was created (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When its values were last edited (UTC). Kept because a profile is mutable and
    /// the revision history therefore cannot answer "when did Night last change?" —
    /// the revisions record only when it was last <em>applied</em>.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
