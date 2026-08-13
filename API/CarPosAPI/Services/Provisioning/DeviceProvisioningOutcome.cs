namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// How a provisioning attempt ended. Both values are expected outcomes rather
/// than faults, which is why they travel as a return value instead of an
/// exception — the controller maps them straight onto status codes.
///
/// Public because it reaches the signature of <see cref="IDeviceProvisioningService"/>,
/// which a public controller has to be able to consume.
/// </summary>
public enum DeviceProvisioningOutcome
{
    /// <summary>The device row was created and its key pair generated. → 201.</summary>
    Created = 0,

    /// <summary>
    /// A device with that id already exists (active or soft-deleted). → 409.
    /// Provisioning is create-only on purpose: silently overwriting the row would
    /// throw away the private key the flashed firmware is already paired with,
    /// breaking a live device. Key rotation goes through the import-device-key CLI.
    /// </summary>
    DuplicateDeviceId,
}
