using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// One rejected fix inside a <see cref="DeliveryAckDto"/>: the envelope id the
/// firmware minted, plus why ingest refused it.
///
/// The reason comes from a closed vocabulary — the
/// <see cref="Services.Ingest.PositionRejectReason"/> names plus the pipeline's own
/// <c>DecryptFailed</c> and <c>StorageRejected</c> — rather than free text, so the
/// firmware can log something meaningful without this ever becoming a channel for
/// arbitrary strings.
/// </summary>
/// <param name="Id">The envelope's correlation id (16 lowercase hex chars).</param>
/// <param name="Reason">The reject reason name, e.g. <c>TimestampOutOfWindow</c>.</param>
public sealed record DeliveryAckRejectionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reason")] string Reason);
