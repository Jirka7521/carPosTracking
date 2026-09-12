namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Mints and validates the secret half of a share URL. Behind an interface so the
/// redeem path depends on the contract rather than on the hashing choices, and so
/// the shape rules can be tested without a database.
/// </summary>
internal interface IShareTokenFactory
{
    /// <summary>Mints a fresh selector/verifier pair.</summary>
    /// <returns>The parts to store, and the one-time token for the URL.</returns>
    ShareToken Create();

    /// <summary>
    /// Splits and shape-checks a token presented by a visitor.
    ///
    /// This runs <em>before</em> any database work, so a malformed token never
    /// becomes a query.
    /// </summary>
    /// <param name="token">The raw <c>selector.verifier</c> string, or null.</param>
    /// <param name="selector">The lookup half, or empty when parsing failed.</param>
    /// <param name="verifier">The authorising half, or empty when parsing failed.</param>
    /// <returns>True when the token is well-formed. Says nothing about whether it exists.</returns>
    bool TryParse(string? token, out string selector, out string verifier);

    /// <summary>Digests a verifier into its stored form.</summary>
    /// <param name="verifier">The Base64Url verifier half.</param>
    /// <returns>Base64 of the SHA-256 digest.</returns>
    string HashVerifier(string verifier);

    /// <summary>
    /// Compares a presented verifier against a stored digest in constant time.
    /// </summary>
    /// <param name="storedHash">The digest from the database.</param>
    /// <param name="presentedVerifier">The verifier half the visitor supplied.</param>
    /// <returns>True when they match.</returns>
    bool VerifierMatches(string storedHash, string presentedVerifier);
}
