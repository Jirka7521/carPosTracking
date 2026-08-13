using System.Security.Cryptography;
using System.Text;
using CarPosAPI.Options;
using CarPosAPI.Services.Ingest;
using CarPosAPI.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarPosAPI.Tests;

/// <summary>
/// Closes the loop on API-driven provisioning: a key pair generated the way
/// <c>DeviceProvisioningService</c> generates one must survive the full journey —
/// exported to PEM, encrypted at rest under the master key, read back, imported —
/// and still decrypt a firmware-identical envelope built against the public half
/// that was handed out for flashing.
///
/// Without this, the endpoint could hand out a perfectly well-formed public key
/// whose positions the ingest can never read, and nothing would notice until a
/// real device was in a real car.
/// </summary>
public sealed class DeviceProvisioningKeyPairTests
{
    private const string DeviceId = "GNSS01";

    /// <summary>The only key size the firmware ecosystem uses.</summary>
    private const int ExpectedRsaKeySizeBits = 3072;

    private static readonly byte[] s_samplePlaintext = Encoding.UTF8.GetBytes(
        """{"device":"GNSS01","latitude_deg":50.123456,"longitude_deg":14.654321,"speed_kmph":42.5,"altitude_m":231.4,"time_utc":"2026-07-14T12:34:56Z"}""");

    /// <summary>Creates a protector with a fresh random 32-byte master key.</summary>
    /// <returns>The protector standing in for the configured one.</returns>
    private static MasterKeyProtector CreateProtector()
    {
        DeviceKeyProtectionOptions options = new DeviceKeyProtectionOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        return new MasterKeyProtector(Microsoft.Extensions.Options.Options.Create(options));
    }

    /// <summary>
    /// Seals a payload exactly as the firmware does (<c>ESP32/src/crypto/PayloadCrypto.cpp</c>):
    /// a fresh AES-256 key wrapped with RSA-OAEP-SHA256, AES-GCM with a 12-byte
    /// nonce and 16-byte tag, no AAD.
    /// </summary>
    /// <param name="receiverPublicKey">The public key the device was flashed with.</param>
    /// <param name="plaintext">Payload to seal.</param>
    /// <returns>The envelope as the codec would hand it to the crypto service.</returns>
    private static DecodedEnvelope SealAsFirmware(RSA receiverPublicKey, byte[] plaintext)
    {
        byte[] aesKey = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        using AesGcm aes = new AesGcm(aesKey, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] wrappedKey = receiverPublicKey.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
        return new DecodedEnvelope(wrappedKey, nonce, ciphertext, tag);
    }

    [Fact]
    public void GeneratedPairSurvivesProtectionAndDecryptsAFirmwareEnvelope()
    {
        // --- what the service does at provisioning time -----------------------
        using RSA generated = RSA.Create(ExpectedRsaKeySizeBits);
        string publicKeyPem = generated.ExportSubjectPublicKeyInfoPem();
        string privateKeyPem = generated.ExportPkcs8PrivateKeyPem();

        MasterKeyProtector protector = CreateProtector();
        byte[] protectedPrivateKey = protector.Protect(privateKeyPem, DeviceId);

        // --- what the firmware does with the PEM it was flashed with ----------
        using RSA flashedPublicKey = RSA.Create();
        flashedPublicKey.ImportFromPem(publicKeyPem);
        DecodedEnvelope envelope = SealAsFirmware(flashedPublicKey, s_samplePlaintext);

        // --- what DeviceRegistry does on the first message --------------------
        string recoveredPem = protector.Unprotect(protectedPrivateKey, DeviceId);
        using RSA ingestKey = RSA.Create();
        ingestKey.ImportFromPem(recoveredPem);

        Assert.Equal(ExpectedRsaKeySizeBits, ingestKey.KeySize);

        PayloadCryptoService service = new PayloadCryptoService(NullLogger<PayloadCryptoService>.Instance);
        DeviceKeyEntry entry = new DeviceKeyEntry
        {
            Id = Guid.NewGuid(),
            DeviceId = DeviceId,
            PrivateKey = ingestKey,
            LoadedAtUtc = DateTime.UtcNow,
        };

        bool success = service.TryDecrypt(entry, envelope, out byte[] plaintext);

        Assert.True(success);
        Assert.Equal(s_samplePlaintext, plaintext);
    }

    [Fact]
    public void ProtectedKeyIsBoundToItsDevice()
    {
        // The device id is the GCM associated data, so a ciphertext copied onto
        // another device's row must fail to decrypt rather than quietly work.
        using RSA generated = RSA.Create(ExpectedRsaKeySizeBits);
        MasterKeyProtector protector = CreateProtector();

        byte[] protectedPrivateKey = protector.Protect(generated.ExportPkcs8PrivateKeyPem(), DeviceId);

        Assert.ThrowsAny<CryptographicException>(
            () => protector.Unprotect(protectedPrivateKey, "GNSS02"));
    }

    [Fact]
    public void ExportedPublicPemCarriesNoPrivateMaterial()
    {
        // The PEM in the response is what goes into a Git-ignored firmware header
        // and onto a device that may be stolen. It must be the public half only.
        using RSA generated = RSA.Create(ExpectedRsaKeySizeBits);

        string publicKeyPem = generated.ExportSubjectPublicKeyInfoPem();

        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", publicKeyPem, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE", publicKeyPem, StringComparison.Ordinal);

        // A key imported from it must be unable to decrypt — proving it is public-only.
        using RSA imported = RSA.Create();
        imported.ImportFromPem(publicKeyPem);
        byte[] wrapped = imported.Encrypt(RandomNumberGenerator.GetBytes(32), RSAEncryptionPadding.OaepSHA256);

        Assert.ThrowsAny<CryptographicException>(
            () => imported.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256));
    }
}
