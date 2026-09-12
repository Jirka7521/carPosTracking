using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Mints and parses the <c>selector.verifier</c> secret that a share URL carries.
///
/// <para>
/// <b>Why two halves.</b> The selector is indexed and the verifier is not, so
/// finding a link is an equality probe on one short column rather than a scan.
/// The split predates the storage decision below and survives it unchanged: it
/// was always as much about the index as about secrecy.
/// </para>
///
/// <para>
/// <b>Both halves are stored in the clear</b> (2026-09-12), so a creator can look
/// a link up again after the one-time reveal is gone. The trade and its cost are
/// set out on <see cref="Data.Entities.ShareLink"/>. Comparison stays
/// constant-time regardless: it is free, and an early-exit comparison would leak
/// through timing how much of a guessed verifier was right, which is the one thing
/// that could make guessing one feasible at all.
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
    internal const char Separator = '.';

    /// <inheritdoc />
    public ShareToken Create()
    {
        // Base64Url rather than plain base64 throughout: the token travels in a URL
        // path, and '+', '/' and '=' would be percent-encoded on the way there and
        // would have to be decoded back before comparing. That mismatch is exactly
        // the kind of bug that shows up as "the link works for me and not for them".
        string selector = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SelectorBytes));
        string verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(VerifierBytes));

        return new ShareToken(selector, verifier, ShareToken.Compose(selector, verifier));
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
    public bool VerifierMatches(string storedVerifier, string presentedVerifier)
    {
        ArgumentNullException.ThrowIfNull(storedVerifier);
        ArgumentNullException.ThrowIfNull(presentedVerifier);

        byte[] expected = Encoding.UTF8.GetBytes(storedVerifier);
        byte[] actual = Encoding.UTF8.GetBytes(presentedVerifier);

        // FixedTimeEquals returns false for a length mismatch without comparing, so
        // the lengths themselves are not hidden — they are fixed by construction and
        // public knowledge anyway. What it does hide is WHICH byte differs, and that
        // is the signal that would otherwise let an attacker walk a verifier out one
        // character at a time instead of guessing all 256 bits at once.
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
