using System.Security.Cryptography;
using System.Text;
using CarPosAPI.Services.Ingest;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarPosAPI.Tests;

/// <summary>
/// Proves the decryptor is byte-compatible with the firmware's scheme by
/// building envelopes exactly the way <c>ESP32/src/crypto/PayloadCrypto.cpp</c>
/// does (RSA-OAEP-SHA256-wrapped fresh AES-256 key; AES-GCM with 12-byte nonce,
/// 16-byte tag, no AAD) and asserting both the happy path and the mandatory
/// failures (tampering, wrong key, wrong wrapped-key length).
/// </summary>
public sealed class PayloadCryptoServiceTests
{
    private static readonly byte[] s_samplePlaintext = Encoding.UTF8.GetBytes(
        """{"device":"GNSS01","latitude_deg":50.123456,"longitude_deg":14.654321,"speed_kmph":42.5,"altitude_m":231.4,"time_utc":"2026-07-14T12:34:56Z"}""");

    /// <summary>Builds a firmware-identical envelope for the given receiver key.</summary>
    /// <param name="receiverPublicKey">Key to wrap the AES key with.</param>
    /// <param name="plaintext">Payload to encrypt.</param>
    /// <param name="aesKeyBytes">AES key length — 32 normally, other sizes for negative tests.</param>
    /// <param name="corruptTag">Whether to flip a tag byte after sealing.</param>
    /// <returns>The decoded envelope, as the codec would hand it to the crypto service.</returns>
    private static DecodedEnvelope BuildEnvelope(
        RSA receiverPublicKey,
        byte[] plaintext,
        int aesKeyBytes = 32,
        bool corruptTag = false)
    {
        byte[] aesKey = RandomNumberGenerator.GetBytes(aesKeyBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        // Only a 32-byte key can drive AesGcm-256; for the wrong-length negative
        // test the ciphertext content is irrelevant (unwrap-length check fires first).
        if (aesKeyBytes == 32)
        {
            using AesGcm aes = new AesGcm(aesKey, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        if (corruptTag)
        {
            tag[0] ^= 0xFF;
        }

        byte[] wrappedKey = receiverPublicKey.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
        return new DecodedEnvelope(wrappedKey, nonce, ciphertext, tag);
    }

    /// <summary>Creates a cache entry around the shared receiver key.</summary>
    /// <returns>The entry (not disposed — the key is shared across tests).</returns>
    private static DeviceKeyEntry CreateEntry()
    {
        return new DeviceKeyEntry
        {
            Id = Guid.NewGuid(),
            DeviceId = "GNSS01",
            PrivateKey = TestKeys.ReceiverKey,
            LoadedAtUtc = DateTime.UtcNow,
        };
    }

    [Fact]
    public void DecryptsFirmwareIdenticalEnvelope()
    {
        PayloadCryptoService service = new PayloadCryptoService(NullLogger<PayloadCryptoService>.Instance);
        DecodedEnvelope envelope = BuildEnvelope(TestKeys.ReceiverKey, s_samplePlaintext);

        bool success = service.TryDecrypt(CreateEntry(), envelope, out byte[] plaintext);

        Assert.True(success);
        Assert.Equal(s_samplePlaintext, plaintext);
    }

    [Fact]
    public void RejectsTamperedTag()
    {
        PayloadCryptoService service = new PayloadCryptoService(NullLogger<PayloadCryptoService>.Instance);
        DecodedEnvelope envelope = BuildEnvelope(TestKeys.ReceiverKey, s_samplePlaintext, corruptTag: true);

        bool success = service.TryDecrypt(CreateEntry(), envelope, out byte[] plaintext);

        Assert.False(success);
        Assert.Empty(plaintext);
    }

    [Fact]
    public void RejectsEnvelopeWrappedForDifferentKey()
    {
        PayloadCryptoService service = new PayloadCryptoService(NullLogger<PayloadCryptoService>.Instance);
        DecodedEnvelope envelope = BuildEnvelope(TestKeys.UnrelatedKey, s_samplePlaintext);

        bool success = service.TryDecrypt(CreateEntry(), envelope, out byte[] plaintext);

        Assert.False(success);
        Assert.Empty(plaintext);
    }

    [Fact]
    public void RejectsUnwrappedKeyOfWrongLength()
    {
        PayloadCryptoService service = new PayloadCryptoService(NullLogger<PayloadCryptoService>.Instance);
        // Wraps a 16-byte "AES key" — RSA unwrap succeeds but the length gate must fire.
        DecodedEnvelope envelope = BuildEnvelope(TestKeys.ReceiverKey, s_samplePlaintext, aesKeyBytes: 16);

        bool success = service.TryDecrypt(CreateEntry(), envelope, out byte[] plaintext);

        Assert.False(success);
        Assert.Empty(plaintext);
    }

    [Fact]
    public void RejectsTamperedCiphertext()
    {
        PayloadCryptoService service = new PayloadCryptoService(NullLogger<PayloadCryptoService>.Instance);
        DecodedEnvelope original = BuildEnvelope(TestKeys.ReceiverKey, s_samplePlaintext);
        byte[] corrupted = original.Ciphertext.ToArray();
        corrupted[0] ^= 0xFF;
        DecodedEnvelope envelope = original with { Ciphertext = corrupted };

        bool success = service.TryDecrypt(CreateEntry(), envelope, out byte[] plaintext);

        Assert.False(success);
        Assert.Empty(plaintext);
    }
}
