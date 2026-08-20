using System.Security.Cryptography;
using CarPosAPI.Services.Provisioning;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the gate on the one key the <em>device</em> owns.
///
/// For telemetry the server holds the private half; for acks it must hold only the
/// public one, because the ack is sealed <em>to</em> the device. That makes the ack
/// import the single place a device secret could be talked into the database — from
/// the CLI, or over HTTP from the dashboard — and both doors go through this class, so
/// the rules are asserted here once rather than trusted twice.
/// </summary>
public sealed class AckPublicKeyValidatorTests
{
    /// <summary>Generates a key of the given size and returns its public SPKI PEM.</summary>
    /// <param name="keySizeBits">RSA modulus size.</param>
    /// <returns>The public half in PEM form.</returns>
    private static string PublicPem(int keySizeBits)
    {
        using RSA key = RSA.Create(keySizeBits);
        return key.ExportSubjectPublicKeyInfoPem();
    }

    [Fact]
    public void AcceptsAnRsa3072PublicKeyAndFingerprintsIt()
    {
        using RSA key = RSA.Create(3072);
        string pem = key.ExportSubjectPublicKeyInfoPem();

        AckPublicKeyValidation result = AckPublicKeyValidator.Validate(pem);

        Assert.True(result.IsValid);
        Assert.Null(result.Error);

        // The fingerprint must be the same string the provisioning payload and the CLI
        // print, or an operator cannot check that the key on the server is the pair of
        // the private key in their Config.h — which is the only check available to them.
        string expected = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
        Assert.Equal(expected, result.Fingerprint);
    }

    [Fact]
    public void RejectsAPrivateKey()
    {
        // The dangerous case: a private PEM imports perfectly well and the acks would
        // even work, so nothing downstream would notice that a device secret had been
        // written into the database and into every provisioning response after it.
        using RSA key = RSA.Create(3072);
        string privatePem = key.ExportPkcs8PrivateKeyPem();

        AckPublicKeyValidation result = AckPublicKeyValidator.Validate(privatePem);

        Assert.False(result.IsValid);
        Assert.Null(result.Fingerprint);
        Assert.Contains("PRIVATE", result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2048)]
    [InlineData(4096)]
    public void RejectsAKeyOfTheWrongSize(int keySizeBits)
    {
        AckPublicKeyValidation result = AckPublicKeyValidator.Validate(PublicPem(keySizeBits));

        Assert.False(result.IsValid);
        Assert.Null(result.Fingerprint);
        Assert.Contains("3072", result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a pem at all")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nnot base64\n-----END PUBLIC KEY-----\n")]
    public void RejectsAnythingThatIsNotAPem(string candidate)
    {
        AckPublicKeyValidation result = AckPublicKeyValidator.Validate(candidate);

        Assert.False(result.IsValid);
        Assert.Null(result.Fingerprint);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
