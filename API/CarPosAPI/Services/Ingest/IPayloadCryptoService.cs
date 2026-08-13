namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Opens firmware encryption envelopes: RSA-OAEP-SHA256 unwraps the per-fix
/// AES key, AES-256-GCM authenticates and decrypts the payload.
/// </summary>
internal interface IPayloadCryptoService
{
    /// <summary>Attempts to decrypt one envelope for one device.</summary>
    /// <param name="device">The cached device identity holding the private key.</param>
    /// <param name="envelope">A structurally validated envelope.</param>
    /// <param name="plaintext">The decrypted payload bytes on success, empty otherwise.</param>
    /// <returns><c>true</c> when decryption and tag authentication succeeded.</returns>
    bool TryDecrypt(DeviceKeyEntry device, DecodedEnvelope envelope, out byte[] plaintext);
}
