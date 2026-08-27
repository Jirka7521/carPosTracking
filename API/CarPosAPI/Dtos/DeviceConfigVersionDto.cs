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
/// the one created with the device, the one the migration seeded for devices that
/// predate remote settings, and every revision the scheduler wrote.
/// </param>
/// <param name="Source">
/// <c>"manual"</c> or <c>"schedule"</c> — what produced this revision. A plain string
/// rather than the <c>ConfigRevisionSource</c> enum so the wire contract does not
/// depend on the application's JSON enum settings, and so a value added later reads as
/// itself in a client that has not been updated instead of as a bare number.
/// </param>
/// <param name="SourceProfileName">
/// Name of the profile a scheduled revision came from, or null. Resolved at read time,
/// so it goes null once that profile is deleted — the revision keeps its values either
/// way, because this is a label, not a lookup.
/// </param>
public sealed record DeviceConfigVersionDto(
    int Version,
    DeviceConfigValuesDto Values,
    DateTime CreatedAt,
    string? CreatedBy,
    string Source,
    string? SourceProfileName);
