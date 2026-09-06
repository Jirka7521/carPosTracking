using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// One device's schedule bundle paired with the MQTT identity to publish it under.
/// The sibling of <see cref="DeviceConfigPublication"/>, and a named record for the
/// same reason: the reconnect sweep builds a list of these and this project does not
/// use <c>var</c> or anonymous types.
/// </summary>
/// <param name="DeviceId">The device's MQTT identity, e.g. <c>GNSS01</c>.</param>
/// <param name="Bundle">The bundle to publish retained.</param>
internal sealed record DeviceScheduleBundlePublication(string DeviceId, DeviceScheduleBundleDto Bundle);
