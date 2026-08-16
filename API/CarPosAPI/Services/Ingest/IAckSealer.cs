using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Seals a delivery ack for one device. Split out from
/// <see cref="MqttAckPublisher"/> so the crypto and the transport stay separately
/// testable — the same division the firmware makes between <c>PayloadCrypto</c> and
/// <c>MqttClient</c>.
/// </summary>
internal interface IAckSealer
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> to the device's ack public key using the
    /// same hybrid scheme the firmware uses in the opposite direction.
    /// </summary>
    /// <param name="device">Cache entry holding the ack public key and its lock.</param>
    /// <param name="plaintext">The UTF-8 JSON ack document.</param>
    /// <returns>
    /// The envelope to publish, or null when the device has no usable ack key or the
    /// seal failed. Null is a "skip the ack" signal, never an error to throw on:
    /// telemetry that is already stored must not be jeopardised by a reply problem.
    /// </returns>
    EncryptionEnvelopeDto? TrySeal(DeviceKeyEntry device, byte[] plaintext);
}
