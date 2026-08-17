using CarPosAPI.Dtos;
using MQTTnet;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Publishes device settings to <c>devices/&lt;id&gt;/config</c>, retained.
///
/// Lives beside the ingest services rather than with the device services because it
/// shares their one broker connection: the client is created inside
/// <see cref="MqttIngestService"/>'s reconnect loop and handed over with
/// <see cref="AttachClient"/>, exactly as <see cref="IAckPublisher"/> does.
/// </summary>
internal interface IConfigPublisher
{
    /// <summary>
    /// Hands over (or clears) the live broker client. Called by the ingest service as
    /// its client comes and goes; passing null makes every later publish a logged
    /// no-op instead of touching a disposed object.
    /// </summary>
    /// <param name="client">The connected client, or null when there is none.</param>
    void AttachClient(IMqttClient? client);

    /// <summary>
    /// Publishes one device's configuration, retained, so the broker replays it the
    /// instant that device subscribes. Never throws: the revision is already committed
    /// by the time this is called, and a transport fault must not turn a saved setting
    /// into a failed request.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity, e.g. <c>GNSS01</c>.</param>
    /// <param name="document">The document to publish.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    /// <returns>True when the broker accepted it; false when it was skipped or failed.</returns>
    Task<bool> PublishAsync(
        string deviceId,
        DeviceConfigDocumentDto document,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-publishes the current configuration of every active device.
    ///
    /// Called after each successful (re)subscribe. Retained messages normally survive
    /// on the broker without help, but a broker that was restarted without persistence
    /// silently forgets them — and a device with deep sleep on would then never learn
    /// its settings, because it is only ever online for a few seconds at a time. This
    /// makes that state self-healing rather than a manual repair.
    /// </summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many devices were published.</returns>
    Task<int> RepublishAllAsync(CancellationToken cancellationToken);
}
