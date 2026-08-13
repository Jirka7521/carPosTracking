namespace CarPosAPI.Dtos;

/// <summary>
/// A device as the dashboard sees it: identity, lifecycle, the caller's own
/// nickname for it, and what the caller may do with it.
///
/// SECURITY: neither <see cref="Data.Entities.Device.PrivateKeyCiphertext"/> nor
/// the public key appears here. The private key must never leave the server at
/// all; the public key and firmware snippet are a separate, permission-gated
/// resource (<c>GET /api/devices/{deviceId}/provisioning</c>) so they are not
/// scattered through every device list.
/// </summary>
/// <param name="DeviceId">
/// The device's MQTT identity, e.g. <c>GNSS01</c> — and the key the whole API is
/// addressed by. The internal <see cref="Data.Entities.Device.Id"/> Guid stays
/// server-side: exposing two identifiers for one thing only invites clients to
/// pick the wrong one.
/// </param>
/// <param name="DisplayName">The shared, provisioning-time friendly name (may be null).</param>
/// <param name="CustomName">
/// The <em>caller's own</em> nickname for the device, or null when they have not
/// set one. Other users do not see it. The UI falls back to
/// <paramref name="DisplayName"/> and then <paramref name="DeviceId"/>.
/// </param>
/// <param name="IsActive">False once the device has been soft-deleted.</param>
/// <param name="CreatedAt">When the device was provisioned (UTC).</param>
/// <param name="DeactivatedAt">When it was soft-deleted (UTC); null while active.</param>
/// <param name="LastSeenAt">
/// When the last accepted fix from this device arrived (UTC), or null if it has
/// never reported. The firmware sends no heartbeat or last-will, so this is the
/// only liveness signal that exists.
/// </param>
/// <param name="LastBatteryPct">
/// Battery state of charge from this device's most recent fix (0–100), or null
/// when it has never reported or reported no battery. The value 0 is the
/// "charging" sentinel, which the dashboard renders as charging. This lets the
/// device grid show a battery level at a glance without loading its positions.
/// </param>
/// <param name="Permissions">The caller's capabilities — UX hints, see <see cref="DevicePermissionsDto"/>.</param>
public sealed record DeviceDto(
    string DeviceId,
    string? DisplayName,
    string? CustomName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? DeactivatedAt,
    DateTime? LastSeenAt,
    int? LastBatteryPct,
    DevicePermissionsDto Permissions);
