namespace CarPosAPI.Services.Sharing;

/// <summary>
/// A freshly minted link secret, in the three forms the three consumers need:
/// the selector to index by, the whole token to put in the URL, and the digest to
/// store.
///
/// The raw verifier deliberately has no property here. It exists only inside
/// <see cref="ShareTokenFactory.Create"/>, long enough to be hashed and
/// concatenated into <see cref="Token"/>, and nothing downstream has a reason to
/// see it again — so nothing downstream is given the chance.
/// </summary>
/// <param name="Selector">Lookup key, stored in the clear.</param>
/// <param name="VerifierHash">Base64 SHA-256 of the verifier — the only stored trace of it.</param>
/// <param name="Token">The full <c>selector.verifier</c> string for the URL. Shown once, never stored.</param>
public sealed record ShareToken(string Selector, string VerifierHash, string Token);
