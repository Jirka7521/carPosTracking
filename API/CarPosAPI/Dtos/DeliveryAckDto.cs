using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// The plaintext delivery acknowledgement sent back to a device on
/// <c>devices/&lt;id&gt;/ack</c> — sealed into an encryption envelope before it ever
/// touches the broker (see <see cref="Services.Ingest.AckSealer"/>).
///
/// This is what closes the loop the firmware previously could not: a QoS-2 PUBCOMP
/// only proves Mosquitto took the message, so without this the device deleted fixes
/// from its SD queue that ingest may have rejected or never stored at all.
///
/// <para>
/// <b>Stored merges inserted and duplicate on purpose.</b> The write path is a
/// batched <c>INSERT … ON CONFLICT DO NOTHING</c> that returns counts, not per-row
/// outcomes, and the device does the same thing either way: drop the fix from its
/// queue. Splitting them would mean a <c>RETURNING</c> clause and a rework of the
/// <c>unnest</c> insert for no device-visible benefit.
/// </para>
/// </summary>
/// <param name="Device">The device id, echoed so the firmware can reject a misrouted ack.</param>
/// <param name="Stored">Envelope ids now durably in the positions table (inserted or already present).</param>
/// <param name="Rejected">Envelope ids ingest refused, each with its reason.</param>
public sealed record DeliveryAckDto(
    [property: JsonPropertyName("device")] string Device,
    [property: JsonPropertyName("stored")] IReadOnlyList<string> Stored,
    [property: JsonPropertyName("rejected")] IReadOnlyList<DeliveryAckRejectionDto> Rejected);
