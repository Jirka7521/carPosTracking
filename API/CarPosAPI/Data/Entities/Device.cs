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

    /// <summary>Soft-delete flag — rows are deactivated, never physically removed.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the device was soft-deleted (UTC); null while active.</summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>Creation timestamp (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }
}
