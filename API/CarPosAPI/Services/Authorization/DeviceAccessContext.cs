using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Authorization;

/// <summary>
/// The answer to "may this user touch this device, and how far?" — resolved once
/// per request by <see cref="IDeviceAccessAuthorizer"/> and then passed around
/// instead of being re-queried.
///
/// It carries the internal row id as well as the MQTT identity because callers
/// need both: the wire speaks <see cref="DeviceId"/>, the foreign keys speak
/// <see cref="DeviceRowId"/>. Resolving the pair in one place is what keeps every
/// other query from having to translate between them.
/// </summary>
/// <param name="DeviceRowId">Internal <see cref="Data.Entities.Device.Id"/> for joins.</param>
/// <param name="DeviceId">The device's MQTT identity, as it appears on the wire.</param>
/// <param name="IsActive">False when the device has been soft-deleted.</param>
/// <param name="Permissions">What the caller may do, from their active grant.</param>
public sealed record DeviceAccessContext(
    Guid DeviceRowId,
    string DeviceId,
    bool IsActive,
    DevicePermissionsDto Permissions);
