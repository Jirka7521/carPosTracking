namespace CarPosAPI.Dtos;

/// <summary>
/// Response body of a successful <c>POST /api/devices</c>: the new device as it
/// will appear in every later listing, plus the one-time provisioning block for
/// flashing the firmware.
///
/// The two are returned together because they are needed together — the dashboard
/// adds the card to the grid and shows the <c>Config.h</c> snippet in the same
/// step. Splitting them would mean a second round-trip at exactly the moment the
/// user is waiting to copy something.
/// </summary>
/// <param name="Device">The device row, with the creator's (full) permissions.</param>
/// <param name="Provisioning">
/// Public key, fingerprint, topics and the firmware config block. Re-readable
/// later through <c>GET /api/devices/{deviceId}/provisioning</c> — it contains no
/// secret, so there is no reason to make the user copy it under pressure.
/// </param>
public sealed record DeviceCreatedDto(
    DeviceDto Device,
    DeviceProvisioningResultDto Provisioning);
