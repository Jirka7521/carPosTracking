namespace CarPosAPI.Services.Authorization;

/// <summary>
/// The single gate every device-scoped operation passes through.
///
/// A validated JWT says <em>who</em> the caller is and nothing more; it never says
/// what they may touch. This service answers that second question from the
/// database on every request, which is why a share revoked a second ago takes
/// effect immediately rather than when some token expires. Any endpoint that
/// reads or writes a device, its positions or its grants without calling this is
/// a vulnerability, not an oversight.
/// </summary>
public interface IDeviceAccessAuthorizer
{
    /// <summary>Resolves a caller's access to one device.</summary>
    /// <param name="userId">The authenticated caller.</param>
    /// <param name="deviceId">The device's MQTT identity, straight off the wire.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The caller's context, or <c>null</c> when the device does not exist
    /// <em>or</em> the caller holds no active grant on it. The two cases are
    /// deliberately indistinguishable so callers cannot probe for which devices
    /// exist.
    /// </returns>
    Task<DeviceAccessContext?> ResolveAsync(int userId, string deviceId, CancellationToken cancellationToken);
}
