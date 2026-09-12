using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// The link secret: its shape, its uniqueness, and what it refuses.
///
/// <see cref="ShareTokenFactory.TryParse"/> is the first thing a visitor's input
/// touches, and it runs before any database work — so everything it rejects is a
/// query that never happens. These tests are mostly about that boundary.
/// </summary>
public sealed class ShareTokenFactoryTests
{
    private readonly ShareTokenFactory _factory = new ShareTokenFactory();

    [Fact]
    public void CreatedTokenSplitsBackIntoItsHalves()
    {
        ShareToken token = _factory.Create();

        Assert.True(_factory.TryParse(token.Token, out string selector, out string verifier));
        Assert.Equal(token.Selector, selector);
        Assert.Equal(token.VerifierHash, _factory.HashVerifier(verifier));
    }

    [Fact]
    public void CreatedTokenHasTheDocumentedShape()
    {
        ShareToken token = _factory.Create();

        // 22 characters of Base64Url is 16 bytes; 43 is 32. The column widths in
        // ShareLinkConfiguration state the same fact, so a change here that is not
        // mirrored there fails at the database instead of at this assertion.
        Assert.Equal(22, token.Selector.Length);
        Assert.Equal(22 + 1 + 43, token.Token.Length);
        Assert.Equal(44, token.VerifierHash.Length);
        Assert.Equal('.', token.Token[22]);
    }

    [Fact]
    public void EveryTokenIsDifferent()
    {
        // Not a proof of randomness — nothing this cheap is. It catches the failure
        // that actually happens: a factory accidentally made stateless-but-constant
        // by caching, seeding, or a copied buffer.
        HashSet<string> selectors = new HashSet<string>(StringComparer.Ordinal);

        for (int attempt = 0; attempt < 200; attempt++)
        {
            Assert.True(selectors.Add(_factory.Create().Selector));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator-at-all")]
    [InlineData("short.short")]
    // Two separators: the second half would not be a verifier this factory made.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA.BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.C")]
    // Right lengths, wrong alphabet — a '/' would be a path segment and a '%' an
    // escape, and neither has any business reaching a query parameter.
    [InlineData("AAAAAAAAAAAAAAAAAAAA/.BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA.BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB%BBB")]
    public void MalformedTokensAreRefusedWithoutTouchingAnything(string? token)
    {
        Assert.False(_factory.TryParse(token, out string selector, out string verifier));
        Assert.Empty(selector);
        Assert.Empty(verifier);
    }

    [Fact]
    public void AWrongVerifierDoesNotMatch()
    {
        ShareToken token = _factory.Create();
        ShareToken other = _factory.Create();

        Assert.True(_factory.TryParse(token.Token, out string _, out string verifier));
        Assert.True(_factory.TryParse(other.Token, out string _, out string otherVerifier));

        Assert.True(_factory.VerifierMatches(token.VerifierHash, verifier));
        Assert.False(_factory.VerifierMatches(token.VerifierHash, otherVerifier));
    }

    [Fact]
    public void ACorruptStoredHashFailsClosed()
    {
        // A hand-edited or truncated row must not throw its way out of the redeem
        // path, and must certainly not be treated as a match.
        ShareToken token = _factory.Create();

        Assert.True(_factory.TryParse(token.Token, out string _, out string verifier));
        Assert.False(_factory.VerifierMatches("this is not base64 at all!!", verifier));
    }
}
