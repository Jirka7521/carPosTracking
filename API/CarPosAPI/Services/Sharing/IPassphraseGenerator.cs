namespace CarPosAPI.Services.Sharing;

/// <summary>
/// Produces and canonicalises the code that guards a share link. Behind an
/// interface so the redeem path never depends on the alphabet, and so the shape
/// of a generated code can be asserted in a test without a database.
/// </summary>
internal interface IPassphraseGenerator
{
    /// <summary>
    /// Generates a fresh code, shown to the creator exactly once and stored only
    /// as a PBKDF2 hash.
    /// </summary>
    /// <returns>A grouped, unambiguous code such as <c>4XKD-9TQM-R7VP</c>.</returns>
    string Generate();

    /// <summary>
    /// Normalises a code as typed into the form it is hashed and compared in.
    ///
    /// People retype these from a message into a phone keyboard, which supplies
    /// lower case, drops the hyphens, or leaves a trailing space. None of that is
    /// a wrong answer, so none of it should burn an attempt against the cooldown.
    /// The alphabet has no lower-case members and no punctuation, so folding both
    /// away loses nothing that distinguishes one code from another.
    /// </summary>
    /// <param name="passphrase">The code exactly as the visitor typed it.</param>
    /// <returns>The canonical form.</returns>
    string Normalise(string passphrase);
}
