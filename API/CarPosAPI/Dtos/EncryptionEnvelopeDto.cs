using System.Text.Json.Serialization;

namespace CarPosAPI.Dtos;

/// <summary>
/// One encryption envelope exactly as the firmware publishes it (an MQTT message
/// is a JSON array of these — see <c>ESP32/src/crypto/PayloadCrypto.cpp</c>).
/// All members are nullable strings: presence and shape are enforced by
/// <see cref="Services.Ingest.EnvelopeCodec"/> after parsing, so a missing field
/// becomes a clean rejection instead of a serializer exception. Unknown JSON
/// members are deliberately tolerated — a future firmware field must not brick
/// ingestion.
/// </summary>
/// <param name="Algorithm">Declared scheme; must equal <c>RSA-OAEP-SHA256+AES-256-GCM</c>.</param>
/// <param name="WrappedKey">Base64 RSA-3072-OAEP-SHA256-encrypted 32-byte AES key (decodes to 384 bytes).</param>
/// <param name="Nonce">Base64 12-byte AES-GCM nonce.</param>
/// <param name="Ciphertext">Base64 AES-256-GCM ciphertext of the position JSON.</param>
/// <param name="Tag">Base64 16-byte GCM authentication tag.</param>
/// <param name="Id">
/// Cleartext per-envelope correlation id (16 lowercase hex chars) that the
/// firmware mints before sealing, so the delivery ack published back to
/// <c>devices/&lt;id&gt;/ack</c> can name exactly which fixes were stored and which
/// were rejected. It sits *outside* the ciphertext on purpose: the firmware's SD
/// queue stores the envelope verbatim, so the id survives a reboot and lets the
/// device match an ack against a backlog it sealed days earlier.
///
/// Optional: firmware predating the ack protocol sends none, and such an envelope
/// is still ingested normally — it simply cannot be acknowledged.
/// </param>
public sealed record EncryptionEnvelopeDto(
    [property: JsonPropertyName("alg")] string? Algorithm,
    [property: JsonPropertyName("k")] string? WrappedKey,
    [property: JsonPropertyName("iv")] string? Nonce,
    [property: JsonPropertyName("ct")] string? Ciphertext,
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("id")] string? Id = null);
