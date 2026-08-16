using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Implements the firmware's hybrid scheme byte-for-byte
/// (<c>ESP32/src/crypto/PayloadCrypto.cpp</c>): RSA-3072 OAEP with SHA-256 (hash
/// and MGF1, no label — .NET's <see cref="RSAEncryptionPadding.OaepSHA256"/> is
/// exactly mbedTLS's PKCS#1 v2.1 + SHA-256 construction) unwraps a fresh 32-byte
/// AES key, then AES-256-GCM (12-byte nonce, 16-byte tag, no AAD) opens the
/// payload. Every failure returns <c>false</c> — a bad envelope is data to drop,
/// never an exception to crash on — and no failure detail beyond the exception
/// type is logged, so nothing key- or plaintext-shaped can leak into logs.
/// </summary>
internal sealed class PayloadCryptoService : IPayloadCryptoService
{
    /// <summary>The unwrapped AES key must be exactly 32 bytes (AES-256).</summary>
    private const int AesKeyBytes = 32;

    /// <summary>GCM tag length in bytes, fixed by the firmware.</summary>
    private const int TagBytes = 16;

    private readonly ILogger<PayloadCryptoService> _logger;

    /// <summary>Creates the crypto service.</summary>
    /// <param name="logger">Structured logger (never receives key material).</param>
    public PayloadCryptoService(ILogger<PayloadCryptoService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryDecrypt(DeviceKeyEntry device, DecodedEnvelope envelope, out byte[] plaintext)
    {
        plaintext = Array.Empty<byte>();

        byte[] aesKey;
        try
        {
            // RSA members are not guaranteed thread-safe; the per-device lock makes
            // the cached instance safe even if processing ever becomes concurrent.
            lock (device.DecryptLock)
            {
                aesKey = device.PrivateKey.Decrypt(envelope.WrappedKey, RSAEncryptionPadding.OaepSHA256);
            }
        }
        catch (CryptographicException exception)
        {
            _logger.LogWarning(
                "RSA unwrap failed for device {DeviceId}: {ExceptionType}",
                device.DeviceId,
                exception.GetType().Name);
            return false;
        }

        try
        {
            if (aesKey.Length != AesKeyBytes)
            {
                _logger.LogWarning(
                    "Unwrapped key for device {DeviceId} has unexpected length {Length}",
                    device.DeviceId,
                    aesKey.Length);
                return false;
            }

            byte[] decrypted = new byte[envelope.Ciphertext.Length];
            using AesGcm aes = new AesGcm(aesKey, TagBytes);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, decrypted);
            plaintext = decrypted;
            return true;
        }
        catch (CryptographicException exception)
        {
            // Covers AuthenticationTagMismatchException — a tampered or corrupt envelope.
            _logger.LogWarning(
                "AES-GCM decrypt failed for device {DeviceId}: {ExceptionType}",
                device.DeviceId,
                exception.GetType().Name);
            return false;
        }
        finally
        {
            // The per-fix AES key is single-use; wipe it the moment it is spent.
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }
}
