namespace CarPosAPI.Data.Entities;

/// <summary>
/// One entry in a device's connection history: it went offline (and why), or it came
/// back from a reset worth knowing about. Written only by the status ingest
/// (<see cref="Services.Ingest.DeviceStatusWriter"/>) from the device's own status
/// messages and its Last Will; read by the dashboard's Events tab and status badge.
/// Mapped by <see cref="Configurations.DeviceEventConfiguration"/>.
///
/// <para>
/// <b>Kind, reason and severity are strings, not enums</b> — a deliberate exception to
/// the int-enum convention <see cref="ShareScope"/> follows. These three ARE the wire
/// vocabulary (<see cref="Dtos.DeviceEventKindNames"/> and friends), stored exactly as
/// the dashboard receives them, so the read side projects them straight into DTOs inside
/// the device-list query without a mapping EF would have to translate. The database
/// pins each to its vocabulary with a CHECK constraint, so a free-text column this is
/// not.
/// </para>
/// </summary>
public sealed class DeviceEvent
{
    /// <summary>Surrogate key (bigint identity) — also the stable order within one instant.</summary>
    public long Id { get; set; }

    /// <summary>Owning device (FK, delete restricted — the history outlives nothing).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Navigation to the owning device.</summary>
    public Device? Device { get; set; }

    /// <summary>
    /// When the server received the message (UTC). The event's authoritative time: the
    /// Last Will carries no clock, and every other kind is published at the moment it
    /// describes.
    /// </summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>The device's own clock at the time (UTC), when it trusted one.</summary>
    public DateTime? DeviceTime { get; set; }

    /// <summary><see cref="Dtos.DeviceEventKindNames"/> — offline or restart.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary><see cref="Dtos.DeviceEventReasonNames"/>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary><see cref="Dtos.DeviceEventSeverityNames"/>, decided server-side.</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// The battery percent the device last knew, or null. 0 is the charging sentinel,
    /// exactly as in <see cref="Position.BatteryPct"/>. CHECK-constrained to [0, 100].
    /// </summary>
    public int? BatteryPct { get; set; }

    /// <summary>
    /// How long the device expected to be away, in seconds, or null when it did not
    /// say. Lets the dashboard show when to expect it back — and notice when it is late.
    /// </summary>
    public int? SleepSeconds { get; set; }

    /// <summary>A short machine code qualifying an error (e.g. <c>gnss_init</c>), or null.</summary>
    public string? Detail { get; set; }
}
