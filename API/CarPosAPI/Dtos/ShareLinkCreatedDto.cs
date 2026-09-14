namespace CarPosAPI.Dtos;

/// <summary>
/// The one and only response that ever carries a share link's secrets.
///
/// <para>
/// Both halves are returned together and stored nowhere in recoverable form: the
/// verifier survives in the database as a SHA-256 digest and the passphrase as a
/// PBKDF2 hash. If this response is lost, the link is unusable and must be
/// reissued. That is the intended behaviour, not a gap — a system that can show
/// you the code again is a system where the code can be taken from it.
/// </para>
///
/// <para>
/// The <paramref name="Token"/> is not a URL. The frontend composes one from the
/// origin and base path it is already running under, which keeps a "public base
/// URL" setting — one more thing to get wrong behind the deployment's path prefix,
/// and wrong in a way that produces links nobody can open — out of existence.
/// </para>
/// </summary>
/// <param name="Share">The link itself, in the same shape the management list uses.</param>
/// <param name="Token">The <c>selector.verifier</c> secret for the URL. Shown once.</param>
/// <param name="Passphrase">The code the visitor must type. Shown once.</param>
public sealed record ShareLinkCreatedDto(
    ShareLinkDto Share,
    string Token,
    string Passphrase);
