namespace CarPosAPI.Services.Ingest;

/// <summary>
/// One envelope after base64 decoding and structural checks by
/// <see cref="EnvelopeCodec"/> — byte arrays with firmware-guaranteed lengths,
/// ready for <see cref="PayloadCryptoService"/>.
/// </summary>
/// <param name="WrappedKey">RSA-OAEP-encrypted AES key (exactly 384 bytes — RSA-3072).</param>
/// <param name="Nonce">AES-GCM nonce (exactly 12 bytes).</param>
/// <param name="Ciphertext">AES-GCM ciphertext (1 .. MaxCiphertextBytes).</param>
/// <param name="Tag">AES-GCM authentication tag (exactly 16 bytes).</param>
/// <param name="Id">
/// The firmware's correlation id, already shape-checked (16 lowercase hex chars),
/// or null when the envelope carried none. <see cref="IngestPipeline"/> keys the
/// delivery ack on this; a null id means the envelope is ingested but silently
/// unacknowledgeable, which is the intended behaviour for pre-ack firmware.
/// </param>
internal sealed record DecodedEnvelope(
    byte[] WrappedKey,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag,
    string? Id);
