using System.Security.Cryptography;
using CarPosAPI.Options;
using CarPosAPI.Services.Security;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the step a master-key rotation is made of. The failure that matters is
/// silent loss: a rotation that stored a blob the new key cannot open would leave a
/// tracker whose telemetry can never be read again. So beyond the round trip, every
/// way the old blob must refuse to move — wrong current key, another device's id —
/// is asserted to throw rather than to produce something storable.
/// </summary>
public sealed class MasterKeyRewrapperTests
{
    private const string SamplePlaintext = "-----BEGIN PRIVATE KEY-----\nnot-a-real-key\n-----END PRIVATE KEY-----";

    private const string DeviceId = "GNSS01";

    /// <summary>Creates a protector over a fresh random 32-byte master key.</summary>
    /// <returns>The protector.</returns>
    private static MasterKeyProtector CreateProtector()
    {
        DeviceKeyProtectionOptions options = new DeviceKeyProtectionOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };

        // Fully qualified: the CarPosAPI.Options namespace shadows the static
        // Microsoft.Extensions.Options.Options class this needs.
        return new MasterKeyProtector(Microsoft.Extensions.Options.Options.Create(options));
    }

    [Fact]
    public void MovesTheBlobToTheNewKey()
    {
        MasterKeyProtector oldKey = CreateProtector();
        MasterKeyProtector newKey = CreateProtector();
        byte[] blob = oldKey.Protect(SamplePlaintext, DeviceId);

        byte[] rewrapped = MasterKeyRewrapper.Rewrap(oldKey, newKey, blob, DeviceId);

        // Opens under the new key, to exactly the original…
        Assert.Equal(SamplePlaintext, newKey.Unprotect(rewrapped, DeviceId));

        // …and no longer under the old one: that is what makes the old key worthless.
        Assert.ThrowsAny<CryptographicException>(() => oldKey.Unprotect(rewrapped, DeviceId));
    }

    [Fact]
    public void RefusesWhenTheCurrentKeyIsWrong()
    {
        MasterKeyProtector actualKey = CreateProtector();
        MasterKeyProtector wrongKey = CreateProtector();
        byte[] blob = actualKey.Protect(SamplePlaintext, DeviceId);

        Assert.ThrowsAny<CryptographicException>(
            () => MasterKeyRewrapper.Rewrap(wrongKey, CreateProtector(), blob, DeviceId));
    }

    [Fact]
    public void KeepsTheBlobBoundToItsOwnDevice()
    {
        MasterKeyProtector oldKey = CreateProtector();
        MasterKeyProtector newKey = CreateProtector();
        byte[] blob = oldKey.Protect(SamplePlaintext, DeviceId);

        // Presented under another device's id, the blob must not move at all…
        Assert.ThrowsAny<CryptographicException>(
            () => MasterKeyRewrapper.Rewrap(oldKey, newKey, blob, "GNSS02"));

        // …and once moved under its own id, it must still refuse every other one.
        byte[] rewrapped = MasterKeyRewrapper.Rewrap(oldKey, newKey, blob, DeviceId);
        Assert.ThrowsAny<CryptographicException>(() => newKey.Unprotect(rewrapped, "GNSS02"));
    }
}
