using System.Security.Cryptography;
using CarPosAPI.Options;
using CarPosAPI.Services.Security;
using Microsoft.Extensions.Options;

namespace CarPosAPI.Tests;

/// <summary>
/// Exercises the at-rest encryption blob format: round trip, and — more
/// importantly — every way a blob must FAIL to decrypt (tampering, wrong AAD,
/// wrong key, wrong version), because those failures are the security property.
/// </summary>
public sealed class MasterKeyProtectorTests
{
    private const string SamplePlaintext = "-----BEGIN PRIVATE KEY-----\nnot-a-real-key\n-----END PRIVATE KEY-----";

    private const string DeviceId = "GNSS01";

    /// <summary>Creates a protector with a fresh random 32-byte master key.</summary>
    /// <returns>The protector under test.</returns>
    private static MasterKeyProtector CreateProtector()
    {
        DeviceKeyProtectionOptions options = new DeviceKeyProtectionOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        return new MasterKeyProtector(Microsoft.Extensions.Options.Options.Create(options));
    }

    [Fact]
    public void ProtectThenUnprotectRoundTrips()
    {
        MasterKeyProtector protector = CreateProtector();

        byte[] blob = protector.Protect(SamplePlaintext, DeviceId);
        string recovered = protector.Unprotect(blob, DeviceId);

        Assert.Equal(SamplePlaintext, recovered);
        // Blob must be version byte + 12-byte nonce + ciphertext + 16-byte tag.
        Assert.Equal(0x01, blob[0]);
        Assert.Equal(1 + 12 + SamplePlaintext.Length + 16, blob.Length);
    }

    [Fact]
    public void UnprotectWithDifferentAssociatedDataFails()
    {
        MasterKeyProtector protector = CreateProtector();
        byte[] blob = protector.Protect(SamplePlaintext, DeviceId);

        // A blob copied onto another device's row must not decrypt.
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(blob, "GNSS02"));
    }

    [Fact]
    public void UnprotectTamperedCiphertextFails()
    {
        MasterKeyProtector protector = CreateProtector();
        byte[] blob = protector.Protect(SamplePlaintext, DeviceId);
        blob[blob.Length / 2] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(blob, DeviceId));
    }

    [Fact]
    public void UnprotectUnknownVersionFails()
    {
        MasterKeyProtector protector = CreateProtector();
        byte[] blob = protector.Protect(SamplePlaintext, DeviceId);
        blob[0] = 0x02;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(blob, DeviceId));
    }

    [Fact]
    public void UnprotectTruncatedBlobFails()
    {
        MasterKeyProtector protector = CreateProtector();
        byte[] blob = protector.Protect(SamplePlaintext, DeviceId);
        byte[] truncated = blob.AsSpan(0, 10).ToArray();

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(truncated, DeviceId));
    }

    [Fact]
    public void UnprotectWithDifferentMasterKeyFails()
    {
        MasterKeyProtector first = CreateProtector();
        MasterKeyProtector second = CreateProtector();
        byte[] blob = first.Protect(SamplePlaintext, DeviceId);

        Assert.ThrowsAny<CryptographicException>(() => second.Unprotect(blob, DeviceId));
    }
}
