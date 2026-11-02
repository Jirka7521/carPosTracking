using System.Text.RegularExpressions;
using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins how share-link secrets are kept at rest: digests, never the values — and
/// the one property the migration of existing links depends on, that SQL can
/// reproduce the same digest of the same normalised code.
/// </summary>
public sealed class ShareSecretHashTests
{
    [Fact]
    public void TheDigestIsNotTheSecretAndHasAFixedShape()
    {
        string digest = ShareSecretHash.Of("verifier-value");

        Assert.Matches("^[0-9a-f]{64}$", digest);
        Assert.DoesNotContain("verifier-value", digest, StringComparison.Ordinal);
        Assert.True(ShareSecretHash.Matches(digest, "verifier-value"));
        Assert.False(ShareSecretHash.Matches(digest, "verifier-valuf"));
        Assert.False(ShareSecretHash.Matches("not a digest", "verifier-value"));
    }

    [Fact]
    public void TheDigestIsPlainSha256OfUtf8AsTheMigrationComputesIt()
    {
        // The migration converts existing rows with
        //   encode(sha256(convert_to(value, 'UTF8')), 'hex')
        // so every live link keeps working. This is that function's known answer
        // for "abc" (FIPS 180-2 test vector).
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ShareSecretHash.Of("abc"));
    }

    [Fact]
    public void TheMigrationsNormalisationMatchesTheGenerators()
    {
        // The migration normalises a stored code in SQL before hashing it:
        //   regexp_replace(upper(passphrase), '[^ABCDEFGHJKMNPQRSTUVWXYZ2-9]', '', 'g')
        // which has to agree with PassphraseGenerator.Normalise for every code the
        // generator can have stored, or those links would stop matching.
        PassphraseGenerator generator = new PassphraseGenerator();

        for (int attempt = 0; attempt < 200; attempt++)
        {
            string code = generator.Generate();
            string sqlEquivalent = Regex.Replace(code.ToUpperInvariant(), "[^ABCDEFGHJKMNPQRSTUVWXYZ2-9]", string.Empty);

            Assert.Equal(generator.Normalise(code), sqlEquivalent);
        }
    }
}
