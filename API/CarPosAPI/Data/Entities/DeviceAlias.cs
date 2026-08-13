namespace CarPosAPI.Data.Entities;

/// <summary>
/// A user's private nickname for a device ("Dad's van"). Deliberately a separate
/// table from <see cref="Device.DisplayName"/>: that one is set once at
/// provisioning time and is the same for everybody, whereas this is per-user and
/// changing it must not affect what anyone else sees. A user with read access is
/// allowed to set their own alias, which would be an authorisation problem if it
/// wrote to the shared device row.
///
/// Absence of a row means "no alias" — the UI then falls back to the device's
/// display name and finally to its MQTT id.
/// Mapped by <see cref="Configurations.DeviceAliasConfiguration"/>.
/// </summary>
public sealed class DeviceAlias
{
    /// <summary>Surrogate key (int identity).</summary>
    public int Id { get; set; }

    /// <summary>The user the alias belongs to (FK). Part of the unique pair.</summary>
    public int UserId { get; set; }

    /// <summary>Navigation to the owning user.</summary>
    public User? User { get; set; }

    /// <summary>The device being nicknamed (FK, internal Guid). Part of the unique pair.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Navigation to the device.</summary>
    public Device? Device { get; set; }

    /// <summary>
    /// The nickname itself. Never empty: clearing an alias deletes the row rather
    /// than storing a blank, so "no alias" has exactly one representation.
    /// </summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>When the alias was last written (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
