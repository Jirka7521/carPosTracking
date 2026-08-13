using CarPosAPI.Data;
using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// Creates a device and the RSA key pair the end-to-end encryption is built on.
/// This is the API-driven counterpart of the <c>import-device-key</c> CLI
/// (<see cref="DeviceKeyImportCommand"/>): the CLI imports a pair produced
/// elsewhere, this generates one and hands back only the public half.
/// </summary>
public interface IDeviceProvisioningService
{
    /// <summary>Generates a key pair, stores the device, and describes it for flashing.</summary>
    /// <param name="context">
    /// The caller's context, so the device row and the access grants that make it
    /// visible to somebody are written in <em>one</em> transaction. A device with
    /// no grants would be invisible to every user and un-deletable through the
    /// API, which is exactly the state a half-committed create would leave behind.
    /// </param>
    /// <param name="request">Validated device id and optional display name.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// A <see cref="DeviceProvisioningResult"/> that is either
    /// <see cref="DeviceProvisioningOutcome.Created"/> with the payload, or
    /// <see cref="DeviceProvisioningOutcome.DuplicateDeviceId"/>.
    /// </returns>
    Task<DeviceProvisioningResult> ProvisionAsync(
        CarPosDbContext context,
        CreateDeviceRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-renders the firmware-facing view of an already provisioned device from
    /// its stored public key — the same payload provisioning returned, so a config
    /// block can be recovered without rotating (and thereby bricking) a key pair.
    /// </summary>
    /// <param name="context">Context to read the device row from.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The payload, or null when the device has no stored public key.</returns>
    Task<DeviceProvisioningResultDto?> DescribeAsync(
        CarPosDbContext context,
        string deviceId,
        CancellationToken cancellationToken);
}
