using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// The typed result of <see cref="IDeviceProvisioningService.ProvisionAsync"/>:
/// an outcome plus, on success, the payload the controller returns. Keeps the
/// expected failure (duplicate id) out of the exception path.
///
/// Public for the same reason as <see cref="DeviceProvisioningOutcome"/>: it is
/// part of an interface a public controller consumes.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Device">
/// The provisioning payload — non-null exactly when <paramref name="Outcome"/> is
/// <see cref="DeviceProvisioningOutcome.Created"/>.
/// </param>
/// <param name="DeviceRowId">
/// The new row's internal <see cref="Data.Entities.Device.Id"/>. Not part of the
/// wire contract — it is handed back purely so the caller can attach access grants
/// to the device inside the same transaction without re-querying for it.
/// </param>
public sealed record DeviceProvisioningResult(
    DeviceProvisioningOutcome Outcome,
    DeviceProvisioningResultDto? Device,
    Guid DeviceRowId)
{
    /// <summary>Builds a successful result.</summary>
    /// <param name="device">The payload describing the freshly created device.</param>
    /// <param name="deviceRowId">Internal key of the row just inserted.</param>
    /// <returns>A <see cref="DeviceProvisioningOutcome.Created"/> result.</returns>
    public static DeviceProvisioningResult Created(DeviceProvisioningResultDto device, Guid deviceRowId)
    {
        return new DeviceProvisioningResult(DeviceProvisioningOutcome.Created, device, deviceRowId);
    }

    /// <summary>Builds the duplicate-id result.</summary>
    /// <returns>A <see cref="DeviceProvisioningOutcome.DuplicateDeviceId"/> result.</returns>
    public static DeviceProvisioningResult DuplicateDeviceId()
    {
        return new DeviceProvisioningResult(DeviceProvisioningOutcome.DuplicateDeviceId, null, Guid.Empty);
    }
}
