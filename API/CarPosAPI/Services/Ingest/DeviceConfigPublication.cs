using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// One device's current configuration paired with the MQTT identity to publish it
/// under. A named record rather than an anonymous type because the reconnect sweep in
/// <see cref="MqttConfigPublisher.RepublishAllAsync"/> projects to it inside a LINQ
/// query, and this project does not use <c>var</c>.
/// </summary>
/// <param name="DeviceId">The device's MQTT identity, e.g. <c>GNSS01</c>.</param>
/// <param name="Document">The document to publish retained.</param>
internal sealed record DeviceConfigPublication(string DeviceId, DeviceConfigDocumentDto Document);
