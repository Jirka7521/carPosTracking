using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// The visitor's code: legible when read aloud, forgiving when typed back, and
/// never weaker than generated.
///
/// The normalisation tests matter more than they look. A code that counts a
/// dropped hyphen as a wrong answer burns an attempt against the cooldown, and a
/// few of those turn an honest recipient into someone locked out of a link they
/// were legitimately sent.
/// </summary>
public sealed class PassphraseGeneratorTests
{
    /// <summary>
    /// Characters that must never appear, because a person reading a code down the
    /// phone or copying it off a screen cannot reliably tell them apart.
    /// </summary>
    private const string AmbiguousCharacters = "IL O01";

    private readonly PassphraseGenerator _generator = new PassphraseGenerator();

    [Fact]
    public void GeneratedCodeHasTheDocumentedShape()
    {
        string code = _generator.Generate();

        Assert.Equal("XXXX-XXXX-XXXX".Length, code.Length);
        Assert.Equal(2, code.Count(character => character == '-'));
        Assert.Equal(12, code.Count(char.IsLetterOrDigit));
    }

    [Fact]
    public void GeneratedCodeAvoidsCharactersThatSoundOrLookAlike()
    {
        // Run a few hundred, because a single draw proves nothing about an alphabet.
        for (int attempt = 0; attempt < 300; attempt++)
        {
            string code = _generator.Generate();

            foreach (char ambiguous in AmbiguousCharacters)
            {
                Assert.DoesNotContain(ambiguous, code);
            }
        }
    }

    [Fact]
    public void EveryCodeIsDifferent()
    {
        HashSet<string> codes = new HashSet<string>(StringComparer.Ordinal);

        for (int attempt = 0; attempt < 200; attempt++)
        {
            Assert.True(codes.Add(_generator.Generate()));
        }
    }

    [Theory]
    [InlineData("4XKD-9TQM-R7VP")]
    [InlineData("4xkd-9tqm-r7vp")]
    [InlineData("4XKD9TQMR7VP")]
    [InlineData("  4XKD-9TQM-R7VP  ")]
    [InlineData("4XKD 9TQM R7VP")]
    [InlineData("\"4XKD-9TQM-R7VP\"")]
    public void NormalisationForgivesHowPeopleActuallyType(string typed)
    {
        // Every one of these is the same code as far as the visitor is concerned, so
        // every one must reduce to the same thing: lower case from a phone keyboard,
        // hyphens dropped or swapped for spaces, and the quotes that come along when
        // a code is copied out of a message.
        Assert.Equal("4XKD9TQMR7VP", _generator.Normalise(typed));
    }

    [Fact]
    public void NormalisationIsIdempotent()
    {
        string code = _generator.Generate();
        string once = _generator.Normalise(code);

        Assert.Equal(once, _generator.Normalise(once));
    }

    [Fact]
    public void NormalisationDoesNotMakeDifferentCodesEqual()
    {
        // Folding case and punctuation is safe only because the alphabet contains
        // neither. If somebody widens it to include lower case, this fails.
        Assert.NotEqual(_generator.Normalise("4XKD-9TQM-R7VP"), _generator.Normalise("4XKD-9TQM-R7VQ"));
    }

    [Fact]
    public void AGeneratedCodeMatchesItselfThroughTheStoredDisplayForm()
    {
        // The round trip the redeem path actually performs, and the one that broke
        // when the code moved from a PBKDF2 hash to plain storage: the stored value
        // keeps its hyphens so it can be shown back, while the visitor's typing
        // arrives normalised. Comparing the two raw forms would reject every
        // correctly typed code, and would do it in the one place where failures look
        // like an attack.
        string code = _generator.Generate();

        Assert.Contains('-', code);
        Assert.True(_generator.Matches(code, _generator.Normalise(code)));
    }

    [Theory]
    [InlineData("4XKD-9TQM-R7VP")]
    [InlineData("4xkd 9tqm r7vp")]
    [InlineData("4XKD9TQMR7VP")]
    public void AnyLegibleSpellingOfTheRightCodeMatches(string typed)
    {
        Assert.True(_generator.Matches("4XKD-9TQM-R7VP", _generator.Normalise(typed)));
    }

    [Fact]
    public void AWrongCodeDoesNotMatch()
    {
        Assert.False(_generator.Matches("4XKD-9TQM-R7VP", _generator.Normalise("4XKD-9TQM-R7VQ")));
        Assert.False(_generator.Matches("4XKD-9TQM-R7VP", _generator.Normalise(string.Empty)));

        // A prefix of the right answer must not pass. Constant-time comparison
        // returns false on a length mismatch without comparing at all, which is what
        // makes walking the code out one character at a time impossible.
        Assert.False(_generator.Matches("4XKD-9TQM-R7VP", _generator.Normalise("4XKD")));
    }
}
