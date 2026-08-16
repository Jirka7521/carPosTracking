using CarPosAPI.Dtos;
using MQTTnet;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Publishes delivery acks back to devices. Exists as a singleton because the
/// pipeline (also a singleton) needs to publish, while the only live
/// <see cref="IMqttClient"/> is a local inside <see cref="MqttIngestService"/>'s
/// connect loop — this interface is the seam that hands one to the other without
/// leaking the client into DI, where its lifetime could not be honoured.
/// </summary>
internal interface IAckPublisher
{
    /// <summary>
    /// Adopts the currently connected client, or clears it on disconnect. Called
    /// only by <see cref="MqttIngestService"/>; passing null makes subsequent
    /// publishes no-ops rather than throwing on a dead client.
    /// </summary>
    /// <param name="client">The live client, or null when the link is down.</param>
    void AttachClient(IMqttClient? client);

    /// <summary>
    /// Seals and publishes one ack for <paramref name="device"/>.
    /// </summary>
    /// <param name="device">Device whose ack key seals the message.</param>
    /// <param name="stored">Envelope ids now in the positions table.</param>
    /// <param name="rejected">Envelope ids ingest refused, with reasons.</param>
    /// <param name="cancellationToken">Cancels the publish.</param>
    /// <returns>
    /// A task that completes when the ack has been published or deliberately
    /// skipped. Never faults on a transport or crypto problem: the fixes are
    /// already stored, and a failed ack merely costs the device one retry.
    /// </returns>
    Task PublishAsync(
        DeviceKeyEntry device,
        IReadOnlyList<string> stored,
        IReadOnlyList<DeliveryAckRejectionDto> rejected,
        CancellationToken cancellationToken);
}
