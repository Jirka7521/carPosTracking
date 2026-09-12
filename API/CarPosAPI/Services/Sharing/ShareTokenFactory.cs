using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Mints and parses the <c>selector.verifier</c> secret that a share URL carries.
///
/// <para>
/// <b>Why two halves rather than one long random string.</b> A single secret
/// forces a choice between two bad options: store it in the clear so it can be
/// looked up by equality, or hash it and scan every row hashing candidates on
/// every request. Splitting it settles both — the selector is an indexed
/// identifier that grants nothing, and the verifier is never stored in a usable
/// form, so a database dump yields no working link. It is the same construction
/// used for "remember me" cookies, and for the same reason.
/// </para>
///
/// <para>
/// <b>Why SHA-256 and not PBKDF2 for the verifier.</b> Key stretching buys time
/// against guessing, and guessing only exists where entropy is low enough to
/// enumerate. The verifier is 256 bits straight from the OS CSPRNG: there is
/// nothing to enumerate, so stretching would tax every redeem to defend against
/// an attack that cannot be mounted. The passphrase is the low-entropy secret in
/// this design, and that one does get PBKDF2 — see
/// <see cref="Auth.IPasswordHasher"/>.
/// </para>
///
/// Stateless, so a singleton.
/// </summary>
internal sealed class ShareTokenFactory : IShareTokenFactory
{
    /// <summary>
    /// Entropy in the selector. Its job is uniqueness, not secrecy, and 128 bits
    /// makes an accidental collision across every link this system will ever mint
    /// indistinguishable from impossible.
    /// </summary>
    private const int SelectorBytes = 16;

    /// <summary>
    /// Entropy in the verifier — the half that actually authorises. 256 bits,
    /// generous on purpose and free, matching <c>SessionCookieWriter</c>'s CSRF
    /// token for the same reason.
    /// </summary>
    private const int VerifierBytes = 32;

    /// <summary>Base64Url of <see cref="SelectorBytes"/> bytes, unpadded.</summary>
    private const int SelectorLength = 22;

    /// <summary>Base64Url of <see cref="VerifierBytes"/> bytes, unpadded.</summary>
    private const int VerifierLength = 43;

    /// <summary>
    /// Separates the halves. A character that is not in the Base64Url alphabet, so
    /// the split is unambiguous and a token containing more than one of them is
    /// malformed by construction.
    /// </summary>
    private const char Separator = '.';

    /// <inheritdoc />
    public ShareToken Create()
    {
        // Base64Url rather than plain base64 throughout: the token travels in a URL
        // path, and '+', '/' and '=' would be percent-encoded on the way there and
        // would have to be decoded back before comparing. That mismatch is exactly
        // the kind of bug that shows up as "the link works for me and not for them".
        string selector = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SelectorBytes));

        byte[] verifierBytes = RandomNumberGenerator.GetBytes(VerifierBytes);

        try
        {
            string verifier = WebEncoders.Base64UrlEncode(verifierBytes);

            return new ShareToken(
                selector,
                HashVerifier(verifier),
                string.Concat(selector, Separator, verifier));
        }
        finally
        {
            // The encoded copy above is an immutable string we cannot scrub, but the
            // buffer it came from we can — one fewer copy of a live credential
            // sitting in a heap dump.
            CryptographicOperations.ZeroMemory(verifierBytes);
        }
    }

    /// <inheritdoc />
    public bool TryParse(string? token, out string selector, out string verifier)
    {
        selector = string.Empty;
        verifier = string.Empty;

        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        // Rejecting on shape before touching the database is what keeps a malformed
        // or oversized token from becoming a query at all. Exact lengths rather than
        // a range: both halves are fixed-width by construction, so anything else is
        // not a token this factory produced.
        int separatorIndex = token.IndexOf(Separator, StringComparison.Ordinal);

        if (separatorIndex != SelectorLength
            || token.Length != SelectorLength + 1 + VerifierLength)
        {
            return false;
        }

        string candidateSelector = token[..SelectorLength];
        string candidateVerifier = token[(SelectorLength + 1)..];

        if (!IsBase64Url(candidateSelector) || !IsBase64Url(candidateVerifier))
        {
            return false;
        }

        selector = candidateSelector;
        verifier = candidateVerifier;
        return true;
    }

    /// <inheritdoc />
    public string HashVerifier(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);

        // Hashing the encoded text rather than the bytes behind it. Either works;
        // this way Create() and the redeem path hash the same kind of value, and
        // there is no decode step on the hot path to get subtly wrong.
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));

        return Convert.ToBase64String(digest);
    }

    /// <inheritdoc />
    public bool VerifierMatches(string storedHash, string presentedVerifier)
    {
        ArgumentNullException.ThrowIfNull(storedHash);
        ArgumentNullException.ThrowIfNull(presentedVerifier);

        byte[] expected;
        byte[] actual;

        try
        {
            expected = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            // A stored hash that is not base64 means a corrupted or hand-edited row.
            // Failing closed is the only safe reading of it.
            return false;
        }

        actual = SHA256.HashData(Encoding.UTF8.GetBytes(presentedVerifier));

        // FixedTimeEquals rather than SequenceEqual: comparing secrets byte by byte
        // with an early exit leaks, through timing, how much of a guess was right —
        // which turns a 256-bit search into a 32-step one.
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>
    /// Checks that every character is in the Base64Url alphabet.
    ///
    /// The lengths are already known to be right by the time this runs, so this is
    /// purely about content: it stops a token carrying a path separator, a percent
    /// escape or a quote from ever reaching a query parameter.
    /// </summary>
    /// <param name="value">The candidate half.</param>
    /// <returns>True when the value is well-formed Base64Url.</returns>
    private static bool IsBase64Url(string value)
    {
        foreach (char character in value)
        {
            bool allowed = character is (>= 'A' and <= 'Z')
                or (>= 'a' and <= 'z')
                or (>= '0' and <= '9')
                or '-'
                or '_';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
