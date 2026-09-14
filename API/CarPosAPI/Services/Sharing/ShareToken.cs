namespace CarPosAPI.Services.Sharing;

/// <summary>
/// A freshly minted link secret, in the three forms its consumers need: the
/// selector to index by, the verifier to store beside it, and the whole token as
/// it appears in the URL.
/// </summary>
/// <param name="Selector">Lookup key, indexed.</param>
/// <param name="Verifier">The authorising half, stored in the clear.</param>
/// <param name="Token">The full <c>selector.verifier</c> string for the URL.</param>
public sealed record ShareToken(string Selector, string Verifier, string Token)
{
    /// <summary>
    /// Rebuilds the URL token from the two stored halves.
    ///
    /// This is what makes "show me the link again" answerable at all: the token is
    /// not a third stored value, it is these two joined back together, so there is
    /// no way for a displayed link to disagree with the one that actually works.
    /// </summary>
    /// <param name="selector">The stored lookup half.</param>
    /// <param name="verifier">The stored authorising half.</param>
    /// <returns>The token as it appears in a share URL.</returns>
    public static string Compose(string selector, string verifier)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(verifier);

        return string.Concat(selector, ShareTokenFactory.Separator, verifier);
    }
}
