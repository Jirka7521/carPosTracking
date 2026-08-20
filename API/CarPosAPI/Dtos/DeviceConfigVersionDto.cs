namespace CarPosAPI.Dtos;

/// <summary>
/// One revision of a device's settings as the dashboard sees it: the values, the
/// number that identifies them, and who saved them when.
/// </summary>
/// <param name="Version">Revision number, unique and increasing per device.</param>
/// <param name="Values">The six settings this revision carries.</param>
/// <param name="CreatedAt">When the revision was saved (UTC).</param>
/// <param name="CreatedBy">
/// Display name of whoever saved it, or null for a revision with no human author —
/// the one created with the device, and the one the migration seeded for devices
/// that predate remote settings.
/// </param>
public sealed record DeviceConfigVersionDto(
    int Version,
    DeviceConfigValuesDto Values,
    DateTime CreatedAt,
    string? CreatedBy);
