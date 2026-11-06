using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Services.Auth;

/// <summary>
/// The one rule for turning an invite code into the digest stored in
/// <c>registration_invites.code_hash</c>, shared by the CLI that mints codes and the
/// registration that redeems them, so the two cannot disagree about what a code is.
///
/// <para>
/// Codes use the share-link passphrase alphabet and format (<c>XXXX-XXXX-XXXX</c>,
/// about 59 bits; <see cref="PassphraseGenerator"/>), and are normalised the same
/// way before hashing: case and anything outside the alphabet (hyphens, spaces, a
/// stray paste of punctuation) are ignored, so a code typed from a phone screen still
/// matches. The digest is <see cref="ShareSecretHash"/>'s SHA-256: 59 random bits
/// leave nothing for a slower hash to protect, and the table holds no other secret.
/// </para>
/// </summary>
internal static class RegistrationInviteCodes
{
    /// <summary>Digests a code as typed (or as minted).</summary>
    /// <param name="passphrases">Supplies the normalisation rule for the code alphabet.</param>
    /// <param name="typed">The code, in any case and with or without separators.</param>
    /// <returns>The digest to store or look up, or null when nothing of a code remains.</returns>
    public static string? Hash(IPassphraseGenerator passphrases, string? typed)
    {
        ArgumentNullException.ThrowIfNull(passphrases);

        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        string normalised = passphrases.Normalise(typed);

        return normalised.Length == 0 ? null : ShareSecretHash.Of(normalised);
    }
}
