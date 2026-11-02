using System.Security.Cryptography;
using System.Text;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// How a share link's verifier and code are kept at rest: as SHA-256 digests,
/// lower-case hex. The plain values exist only in the response that creates or
/// reissues a link, and in whatever the creator does with them.
///
/// <para>
/// A plain, unsalted hash is enough here, unlike for account passwords: the
/// verifier is 256 random bits, so its digest cannot be reversed by search, and the
/// code is useless without the verifier, which is the half a database leak no
/// longer gives away. A fast hash also lets the migration convert existing rows in
/// SQL (<c>sha256()</c>), so no live link breaks.
/// </para>
/// </summary>
internal static class ShareSecretHash
{
    /// <summary>Digests a secret for storage.</summary>
    /// <param name="value">The verifier, or the code already through <see cref="IPassphraseGenerator.Normalise"/>.</param>
    /// <returns>64 lower-case hex characters.</returns>
    public static string Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <summary>Checks a presented secret against a stored digest, in constant time.</summary>
    /// <param name="storedHash">The digest from the database.</param>
    /// <param name="presented">The value the visitor sent (normalised, for a code).</param>
    /// <returns>True when they match.</returns>
    public static bool Matches(string storedHash, string presented)
    {
        ArgumentNullException.ThrowIfNull(storedHash);
        ArgumentNullException.ThrowIfNull(presented);

        // Both sides are fixed-length digests, so FixedTimeEquals hides which byte
        // differs and the length tells nothing.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash),
            Encoding.ASCII.GetBytes(Of(presented)));
    }
}
