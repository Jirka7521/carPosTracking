using CarPosAPI.Data;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

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

    /// <summary>
    /// Stores the <em>public</em> half of an ack key pair generated off-server,
    /// replacing whatever was on the device row before.
    ///
    /// <para>
    /// Rotation is destructive by nature and cannot be undone from here: the private
    /// half exists only in the operator's hands, so a key stored before that half has
    /// been saved into a <c>Config.h</c> leaves the device unable to read the acks the
    /// API will now start sealing to it. Callers must therefore only reach this once
    /// the operator confirms they have kept the file — the dashboard enforces exactly
    /// that ordering.
    /// </para>
    /// </summary>
    /// <param name="context">Context to read and update the device row on.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="ackPublicKeyPem">The candidate key, still unvalidated.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The stored key's fingerprint; <see cref="Common.OperationOutcome.Invalid"/> when
    /// the PEM is not an RSA-3072 public key (or contains private-key material), or
    /// <see cref="Common.OperationOutcome.NotFound"/> when no such device row exists.
    /// </returns>
    Task<OperationResult<AckKeyImportedDto>> ImportAckPublicKeyAsync(
        CarPosDbContext context,
        string deviceId,
        string ackPublicKeyPem,
        CancellationToken cancellationToken);
}
