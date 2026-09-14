namespace CarPosAPI.Services.Privacy;

/// <summary>One private device nickname, joined to the device's MQTT identity.</summary>
/// <param name="DeviceId">The device's MQTT identity.</param>
/// <param name="Alias">The nickname the exporting user chose.</param>
/// <param name="UpdatedAt">When they last changed it (UTC).</param>
public sealed record DeviceAliasExportRow(string DeviceId, string Alias, DateTime UpdatedAt);
