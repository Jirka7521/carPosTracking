namespace CarPosAPI.Data.Entities;

/// <summary>
/// One user's capabilities on one device — the whole authorisation model. There
/// is no ownership column anywhere else: if a user has no active row here for a
/// device, that device does not exist as far as they are concerned. Every
/// device-, position- and sharing-scoped endpoint resolves the caller's row
/// through <see cref="Services.Authorization.IDeviceAccessAuthorizer"/> before acting.
///
/// Two invariants are enforced when rows are written (never trusted from the
/// client): <see cref="CanRead"/> is always true on an active grant, and
/// <see cref="CanShare"/> coerces <see cref="CanModifySettings"/> on — being able
/// to hand out settings rights while lacking them yourself makes no sense.
/// Mapped by <see cref="Configurations.AccessConfiguration"/>.
/// </summary>
public sealed class Access
{
    /// <summary>Surrogate key (int identity). Addressed by <c>/api/access/{id}</c>.</summary>
    public int Id { get; set; }

    /// <summary>The user this grant belongs to (FK).</summary>
    public int UserId { get; set; }

    /// <summary>Navigation to the granted user.</summary>
    public User? User { get; set; }

    /// <summary>
    /// The device the grant is on — the internal <see cref="Device.Id"/> Guid, not
    /// the MQTT identity, so renaming conventions on the wire never touch the FK.
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>Navigation to the device.</summary>
    public Device? Device { get; set; }

    /// <summary>
    /// Who created this grant. Kept for audit ("who let them in?"); it is
    /// deliberately not a foreign key constraint target for deletion purposes —
    /// users are never physically removed either.
    /// </summary>
    public int GrantedBy { get; set; }

    /// <summary>May list the device and read its positions. Always true while active.</summary>
    public bool CanRead { get; set; } = true;

    /// <summary>May soft-delete (deactivate) the device.</summary>
    public bool CanDelete { get; set; }

    /// <summary>May grant, change and revoke other users' access to the device.</summary>
    public bool CanShare { get; set; }

    /// <summary>May change device settings and read the firmware provisioning block.</summary>
    public bool CanModifySettings { get; set; }

    /// <summary>
    /// Soft-revoke flag. Revoking sets this false rather than deleting the row, so
    /// the audit trail of who once had access to a device survives.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the grant was created (UTC). DB-generated default.</summary>
    public DateTime DateRegistration { get; set; }
}
