using CarPosAPI.Dtos;
using MQTTnet;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Publishes device settings to <c>devices/&lt;id&gt;/config</c> and the schedule
/// bundle to <c>devices/&lt;id&gt;/schedule</c>, both retained.
///
/// Lives beside the ingest services rather than with the device services because it
/// shares their one broker connection: the client is created inside
/// <see cref="MqttIngestService"/>'s reconnect loop and handed over with
/// <see cref="AttachClient"/>, exactly as <see cref="IAckPublisher"/> does.
///
/// <para>
/// The two topics carry different things and are not alternatives. The config document
/// is one revision of the seven settings and is what a device without a schedule runs;
/// the bundle is the profiles and windows a device switches between on its own. A
/// device may hold both at once — and does, since the config document is also how a
/// correction reaches a device that has drifted off its schedule.
/// </para>
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
    /// Publishes one device's schedule bundle, retained, so the broker replays it the
    /// instant that device subscribes. Never throws, for the same reason as
    /// <see cref="PublishAsync"/>: the rows are already committed.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity, e.g. <c>GNSS01</c>.</param>
    /// <param name="bundle">The bundle to publish.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    /// <returns>True when the broker accepted it; false when it was skipped or failed.</returns>
    Task<bool> PublishScheduleAsync(
        string deviceId,
        DeviceScheduleBundleDto bundle,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears both retained topics for one device by publishing an empty retained
    /// payload to each — which is how MQTT says "forget what you were holding".
    ///
    /// This exists for erasure. A retained message outlives the row it came from: a
    /// device deleted under GDPR Art. 17 would otherwise leave its settings and its
    /// weekly tracking pattern sitting on the broker indefinitely, readable by anyone
    /// who can subscribe as that device. Never throws, for the same reason as the
    /// publishes above — the rows are already gone by the time this runs.
    /// </summary>
    /// <param name="deviceId">The device's MQTT identity.</param>
    /// <param name="cancellationToken">Cancels the publishes.</param>
    /// <returns>True when the broker accepted both clears.</returns>
    Task<bool> ClearRetainedAsync(string deviceId, CancellationToken cancellationToken);

    /// <summary>
    /// Re-publishes the current configuration <em>and</em> schedule bundle of every
    /// active device.
    ///
    /// Called after each successful (re)subscribe. Retained messages normally survive
    /// on the broker without help, but a broker that was restarted without persistence
    /// silently forgets them — and a device with deep sleep on would then never learn
    /// its settings, because it is only ever online for a few seconds at a time. This
    /// makes that state self-healing rather than a manual repair.
    ///
    /// <para>
    /// The bundle matters here more than the document does. A device that loses its
    /// bundle stops switching on its own and quietly falls back to whatever the config
    /// topic last said — which looks like a working tracker right up until the evening
    /// it fails to go into its low-power profile.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Cancels the sweep.</param>
    /// <returns>How many devices had their configuration published.</returns>
    Task<int> RepublishAllAsync(CancellationToken cancellationToken);
}
