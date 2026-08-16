using System.Security.Cryptography;
using CarPosAPI.Dtos;
using Microsoft.Extensions.Logging;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Produces the encryption envelope for a delivery ack — the exact mirror image of
/// <see cref="PayloadCryptoService"/>. Where that class unwraps a device-sealed
/// envelope with the receiver private key, this one seals an envelope <em>to</em> the
/// device with the device's ack public key, so only firmware holding the matching
/// private key can read it.
///
/// The scheme is deliberately identical to the telemetry direction — RSA-3072
/// OAEP-SHA256 wrapping a fresh 32-byte AES key, then AES-256-GCM with a 12-byte
/// nonce, a 16-byte tag and no AAD — so the firmware needs one envelope format, not
/// two, and the two implementations can be checked against each other.
///
/// Every failure returns null and is logged without detail: an ack that cannot be
/// sealed is an ack not sent, which the device already handles by retrying.
/// </summary>
internal sealed class AckSealer : IAckSealer
{
    /// <summary>AES-256 key size in bytes.</summary>
    private const int AesKeyBytes = 32;

    /// <summary>GCM nonce size in bytes — must match the firmware's reader.</summary>
    private const int NonceBytes = 12;

    /// <summary>GCM tag size in bytes — must match the firmware's reader.</summary>
    private const int TagBytes = 16;

    private readonly ILogger<AckSealer> _logger;

    /// <summary>Creates the sealer.</summary>
    /// <param name="logger">Structured logger (never receives key material).</param>
    public AckSealer(ILogger<AckSealer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public EncryptionEnvelopeDto? TrySeal(DeviceKeyEntry device, byte[] plaintext)
    {
        if (device.AckPublicKey is null)
        {
            return null;
        }

        // One-time key and nonce per ack. Reusing either under the same key would
        // break GCM outright, so both come fresh from the CSPRNG every time.
        byte[] aesKey = RandomNumberGenerator.GetBytes(AesKeyBytes);
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagBytes];

            using (AesGcm aes = new AesGcm(aesKey, TagBytes))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            byte[] wrappedKey;

            // RSA instance members are not guaranteed thread-safe. Ingest is strictly
            // sequential today, so this shares the entry's existing lock rather than
            // inventing a second one — it is a guard rail, not a hot path.
            lock (device.DecryptLock)
            {
                wrappedKey = device.AckPublicKey.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
            }

            return new EncryptionEnvelopeDto(
                EnvelopeCodec.ExpectedAlgorithm,
                Convert.ToBase64String(wrappedKey),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
        }
        catch (CryptographicException exception)
        {
            _logger.LogWarning(
                "Failed to seal delivery ack for device {DeviceId}: {ExceptionType}",
                device.DeviceId,
                exception.GetType().Name);
            return null;
        }
        finally
        {
            // The per-ack AES key is single-use; wipe it the moment it is spent.
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }
}
