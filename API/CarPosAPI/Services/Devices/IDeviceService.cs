using CarPosAPI.Dtos;
using CarPosAPI.Services.Common;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// Device operations as the dashboard needs them: list what a user can see,
/// register a new tracker, retire one, nickname one, and recover its firmware
/// config block. Each method authorises the caller itself — the controller above
/// never decides who may do what.
/// </summary>
public interface IDeviceService
{
    /// <summary>Lists every device the user holds an active grant on.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The devices, including soft-deleted ones — the dashboard has a "show
    /// inactive" toggle and filtering them out here would leave it with nothing to
    /// toggle.
    /// </returns>
    Task<IReadOnlyList<DeviceDto>> ListForUserAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Registers a device, generates its key pair and shares it as requested.</summary>
    /// <param name="userId">The creator, who always receives full access.</param>
    /// <param name="request">Device id, optional name, optional co-owners.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The device and its provisioning payload, or a conflict if the id is taken.</returns>
    Task<OperationResult<DeviceCreatedDto>> CreateAsync(
        int userId,
        CreateDeviceRequestDto request,
        CancellationToken cancellationToken);

    /// <summary>Soft-deletes a device. Requires <c>CanDelete</c>.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success, or the reason the caller may not.</returns>
    Task<OperationResult<bool>> DeactivateAsync(int userId, string deviceId, CancellationToken cancellationToken);

    /// <summary>Sets or clears the caller's private nickname for a device.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="alias">The new nickname; empty or whitespace removes it.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>Success for any caller with read access — an alias is private to them.</returns>
    Task<OperationResult<bool>> SetAliasAsync(
        int userId,
        string deviceId,
        string alias,
        CancellationToken cancellationToken);

    /// <summary>Re-renders the firmware config file. Requires <c>CanModifySettings</c>.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The provisioning payload (public key only — never the private half).</returns>
    Task<OperationResult<DeviceProvisioningResultDto>> GetProvisioningAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores a newly generated ack <em>public</em> key for a device, replacing any
    /// previous one. Requires <c>CanModifySettings</c>.
    /// </summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="request">The candidate public key.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The stored key's fingerprint, or why it was refused.</returns>
    Task<OperationResult<AckKeyImportedDto>> ImportAckKeyAsync(
        int userId,
        string deviceId,
        ImportAckKeyRequestDto request,
        CancellationToken cancellationToken);
}
