using System.Security.Cryptography;

namespace CarPosAPI.Services.Provisioning;

/// <summary>
/// Decides whether a PEM handed in from outside is an acceptable device <em>ack</em>
/// public key, and fingerprints it if so.
///
/// <para>
/// It exists as its own class because two callers need the identical answer: the
/// <c>import-device-key --ack-public-pem</c> CLI, run by someone with a shell on the
/// server, and <c>POST /api/devices/{id}/ack-key</c>, called by the dashboard after
/// generating a pair in the operator's browser. Two copies of these three checks would
/// eventually disagree, and the direction they would disagree in is the dangerous one.
/// </para>
///
/// <para>
/// Pure and stateless — no database, no logging — which is also what makes the rules
/// unit-testable without standing anything up.
/// </para>
/// </summary>
internal static class AckPublicKeyValidator
{
    /// <summary>The only key size the firmware ecosystem uses.</summary>
    private const int ExpectedRsaKeySizeBits = 3072;

    /// <summary>Checks a candidate key and computes its fingerprint.</summary>
    /// <param name="ackPublicKeyPem">The PEM as supplied, unvalidated.</param>
    /// <returns>A fingerprint, or the reason the key was refused.</returns>
    public static AckPublicKeyValidation Validate(string ackPublicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(ackPublicKeyPem))
        {
            return new AckPublicKeyValidation("No key was supplied.", null);
        }

        // Checked before parsing, not after: a private PEM imports perfectly well and
        // would even work, which is exactly what makes it dangerous. Storing one would
        // put a device secret into the database and into every provisioning response
        // thereafter — the ack direction's whole point is that the server never holds
        // this device's private half.
        if (ackPublicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            return new AckPublicKeyValidation(
                "That is a PRIVATE key. Supply the ack PUBLIC key — the private half belongs "
                + "only in the firmware's Config.h and must never reach this server.",
                null);
        }

        using RSA candidate = RSA.Create();

        try
        {
            candidate.ImportFromPem(ackPublicKeyPem);
        }
        catch (ArgumentException)
        {
            // ImportFromPem reports every malformed input this way: no PEM block, an
            // unsupported label, or corrupt base64.
            return new AckPublicKeyValidation("That is not a valid PEM public key.", null);
        }

        if (candidate.KeySize != ExpectedRsaKeySizeBits)
        {
            return new AckPublicKeyValidation(
                $"The ack key is {candidate.KeySize} bits; this system uses RSA-{ExpectedRsaKeySizeBits}.",
                null);
        }

        return new AckPublicKeyValidation(
            null,
            Convert.ToHexString(SHA256.HashData(candidate.ExportSubjectPublicKeyInfo())));
    }
}
