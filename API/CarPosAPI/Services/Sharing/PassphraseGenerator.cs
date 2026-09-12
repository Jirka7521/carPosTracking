using System.Security.Cryptography;
using System.Text;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Generates the code a visitor types before any position is returned.
///
/// <para>
/// <b>The server picks it, not the creator.</b> A code chosen by a person who is
/// about to read it down the phone is a code like "1234" or the dog's name, and
/// it is the only barrier left once a link has been forwarded to someone it was
/// not meant for. Generating it removes that failure mode entirely, at the cost
/// of the creator having to copy one string.
/// </para>
///
/// <para>
/// <b>Shaped to survive being read aloud.</b> The alphabet omits every pair that
/// sounds or looks alike — no <c>I</c>/<c>1</c>/<c>l</c>, no <c>O</c>/<c>0</c> —
/// and the output is grouped with hyphens. A code that gets mistyped twice is a
/// code that trips the cooldown, so legibility here is a security property and
/// not a nicety.
/// </para>
///
/// Stateless, so a singleton.
/// </summary>
internal sealed class PassphraseGenerator : IPassphraseGenerator
{
    /// <summary>
    /// Thirty-one unambiguous characters: the alphabet minus <c>I</c>, <c>L</c> and
    /// <c>O</c>, and the digits minus <c>0</c> and <c>1</c>. Every pair a person
    /// could hear wrong or transcribe wrong is gone, which is what lets
    /// <see cref="Normalise"/> be lossless and lets a tripped cooldown mean
    /// guessing rather than fumbling.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Characters per hyphen-separated group.</summary>
    private const int GroupLength = 4;

    /// <summary>
    /// Number of groups. Twelve characters over a 31-symbol alphabet is roughly 59
    /// bits — far past anything the cooldown ladder would ever let an attacker work
    /// through, and still short enough to dictate in one breath.
    /// </summary>
    private const int GroupCount = 3;

    /// <inheritdoc />
    public string Generate()
    {
        StringBuilder builder = new StringBuilder(GroupCount * (GroupLength + 1) - 1);

        for (int group = 0; group < GroupCount; group++)
        {
            if (group > 0)
            {
                builder.Append('-');
            }

            for (int position = 0; position < GroupLength; position++)
            {
                // GetInt32 draws uniformly over the half-open range and handles the
                // rejection sampling itself, so this stays unbiased whatever the
                // alphabet's length happens to be.
                builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            }
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public string Normalise(string passphrase)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        StringBuilder builder = new StringBuilder(passphrase.Length);

        foreach (char character in passphrase)
        {
            char upper = char.ToUpperInvariant(character);

            // Anything outside the alphabet — hyphens, spaces, an accidental paste of
            // surrounding punctuation — is dropped rather than rejected. The code is
            // still wrong if the letters are wrong.
            if (Alphabet.Contains(upper, StringComparison.Ordinal))
            {
                builder.Append(upper);
            }
        }

        return builder.ToString();
    }
}
