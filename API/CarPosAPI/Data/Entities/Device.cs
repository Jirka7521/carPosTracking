namespace CarPosAPI.Data.Entities;

/// <summary>
/// A registered GNSS tracker. The row is the single source of truth for which
/// devices may deliver positions: an MQTT message whose topic id has no active
/// row here is dropped (no auto-registration — a broker account must not be able
/// to invent devices). Mapped by <see cref="Configurations.DeviceConfiguration"/>.
/// </summary>
public sealed class Device
{
    /// <summary>Internal primary key; FK target for positions. DB-generated.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The device's MQTT identity, e.g. <c>GNSS01</c>. Equals the broker username,
    /// client id, topic segment (<c>devices/GNSS01</c>) and the <c>device</c> field
    /// inside every decrypted payload. Stored exact-case because MQTT topics are
    /// case-sensitive.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Optional human-friendly name shown in future UIs.</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Receiver RSA-3072 public key (PEM). Not needed for decryption — kept so
    /// provisioning tooling and tests can produce firmware-identical envelopes.
    /// </summary>
    public string? PublicKeyPem { get; set; }

    /// <summary>
    /// Receiver RSA-3072 private key PEM, encrypted at rest with the master key
    /// (AES-256-GCM blob, format owned by
    /// <see cref="Services.Security.MasterKeyProtector"/>, AAD = <see cref="DeviceId"/>).
    /// SECRET: must never be selected into a DTO, logged, or exposed by any endpoint.
    /// </summary>
    public byte[]? PrivateKeyCiphertext { get; set; }

    /// <summary>
    /// The device's own RSA-3072 <em>public</em> key (PEM), used to seal the delivery
    /// ack published to <c>devices/&lt;id&gt;/ack</c>.
    ///
    /// Note the roles are the mirror image of <see cref="PrivateKeyCiphertext"/>: for
    /// telemetry the device encrypts and this server decrypts, so the server holds the
    /// private half; for acks the server encrypts and the device decrypts, so the
    /// server holds only the public half. The matching private key is generated
    /// offline and pasted straight into the firmware's git-ignored <c>Config.h</c> —
    /// it deliberately never reaches this database, any DTO, or the dashboard.
    ///
    /// Null means acks are not configured for this device: ingest still stores its
    /// fixes, it just cannot confirm them.
    /// </summary>
    public string? AckPublicKeyPem { get; set; }

    /// <summary>
    /// When the last accepted message from this device arrived (UTC). The firmware
    /// sends no LWT or heartbeat, so this is the only "device is alive" signal the
    /// frontend will ever get.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// The revision of <see cref="DeviceConfigVersion"/> this device is <em>meant</em>
    /// to be running — the one published retained to <c>devices/&lt;id&gt;/config</c>.
    /// Only a pointer: the values themselves live in the version row, so there is
    /// exactly one copy of them and no way for the two to disagree.
    /// </summary>
    public int ConfigVersion { get; set; } = Dtos.DeviceConfigRules.InitialVersion;

    /// <summary>
    /// The revision the device last told us it is <em>actually</em> running, echoed
    /// back in its position reports. Null when it has never reported one — a brand-new
    /// device, or firmware older than the settings-version protocol.
    ///
    /// Equal to <see cref="ConfigVersion"/> means in sync; lower means a change is
    /// published and waiting to be picked up on the device's next report.
    /// </summary>
    public int? ConfigAppliedVersion { get; set; }

    /// <summary>When <see cref="ConfigAppliedVersion"/> was last confirmed (UTC).</summary>
    public DateTime? ConfigAppliedAt { get; set; }

    /// <summary>
    /// Whether <see cref="DeviceConfigScheduleRule"/>s decide this device's settings.
    /// While false the schedule tables may hold anything at all and nothing acts on
    /// them — turning it off is how a schedule is parked without being dismantled.
    /// </summary>
    public bool ConfigScheduleEnabled { get; set; }

    /// <summary>
    /// The profile applied whenever no rule's window contains the current instant
    /// (FK to <c>device_config_profiles.id</c>). Required in practice for an enabled
    /// schedule: without it an uncovered hour would have no defined answer, and the
    /// device would simply keep whatever it last happened to be given.
    /// </summary>
    public Guid? ConfigScheduleFallbackProfileId { get; set; }

    /// <summary>
    /// While this instant is in the future, the scheduler leaves this device alone —
    /// somebody changed its settings by hand and was told the schedule resumes here.
    ///
    /// <para>
    /// A timestamp rather than a flag, and deliberately so: an override expires on
    /// its own. A crash, a restart, or a scheduler that was down over the boundary
    /// cannot leave a tracker stranded off its schedule for ever, which a boolean
    /// somebody forgot to clear certainly could.
    /// </para>
    /// </summary>
    public DateTime? ConfigOverrideUntil { get; set; }

    /// <summary>
    /// When the scheduler last completed a pass over this device (UTC). Null until
    /// the first one. Purely diagnostic — it is what lets the dashboard say "the
    /// schedule is enabled but nothing has evaluated it" rather than showing a
    /// confident answer nobody has computed.
    /// </summary>
    public DateTime? ConfigScheduleEvaluatedAt { get; set; }

    /// <summary>Soft-delete flag — rows are deactivated, never physically removed.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the device was soft-deleted (UTC); null while active.</summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>Creation timestamp (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }
}
