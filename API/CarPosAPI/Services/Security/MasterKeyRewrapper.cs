using System.Security.Cryptography;

namespace CarPosAPI.Services.Security;

/// <summary>
/// Moves one protected blob from the current master key to a new one: decrypt
/// under the current key, encrypt under the new key, and prove the result opens
/// again before anything is allowed to store it.
///
/// <para>
/// The proof is the point of the class. A rotation overwrites the only copy of each
/// device's receiver private key, and a blob that cannot be opened afterwards is a
/// tracker whose telemetry this server can never read again — short of
/// re-provisioning the hardware. Checking that the new blob decrypts to exactly the
/// original costs one AES-GCM operation per device and turns that failure into an
/// exception before the write rather than a silent loss after it.
/// </para>
///
/// <para>
/// The associated data (the device id) is carried across unchanged, so a blob stays
/// bound to its own row under the new key exactly as it was under the old one.
/// </para>
///
/// Pure: two protectors and a blob in, a blob out. No database, no clock — which is
/// what lets the tests hold every failure mode to account.
/// </summary>
internal static class MasterKeyRewrapper
{
    /// <summary>Re-encrypts one blob under the replacement key.</summary>
    /// <param name="current">Protector over the master key the blob is encrypted under now.</param>
    /// <param name="replacement">Protector over the new master key.</param>
    /// <param name="blob">The stored blob.</param>
    /// <param name="associatedData">The blob's associated data — for device keys, the device id.</param>
    /// <returns>The blob encrypted under the replacement key.</returns>
    /// <exception cref="CryptographicException">
    /// The blob does not open under the current key (wrong key, tampering, or the wrong
    /// associated data), or the re-encrypted blob does not round-trip.
    /// </exception>
    public static byte[] Rewrap(
        IMasterKeyProtector current,
        IMasterKeyProtector replacement,
        byte[] blob,
        string associatedData)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentException.ThrowIfNullOrEmpty(associatedData);

        string plaintext = current.Unprotect(blob, associatedData);
        byte[] rewrapped = replacement.Protect(plaintext, associatedData);

        // Opened again with the new key before it can replace the original. Ordinal
        // comparison: a PEM is ASCII, and any difference at all means corruption.
        string roundTrip = replacement.Unprotect(rewrapped, associatedData);

        if (!string.Equals(plaintext, roundTrip, StringComparison.Ordinal))
        {
            throw new CryptographicException("The re-encrypted blob did not decrypt to the original.");
        }

        return rewrapped;
    }
}
