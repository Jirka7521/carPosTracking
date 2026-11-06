using CarPosAPI.Services.Auth;
using CarPosAPI.Services.Sharing;

namespace CarPosAPI.Tests;

/// <summary>
/// Pins the one rule the CLI that mints invite codes and the registration that
/// redeems them must agree on. If they drifted, every code handed out would be
/// refused — or, worse, a code would match something it was never minted as.
/// </summary>
public sealed class RegistrationInviteCodesTests
{
    private static readonly PassphraseGenerator Passphrases = new PassphraseGenerator();

    [Fact]
    public void ACodeTypedLooselyStillMatchesTheOneMinted()
    {
        string minted = Passphrases.Generate();
        string? stored = RegistrationInviteCodes.Hash(Passphrases, minted);

        Assert.NotNull(stored);

        // Read off a phone screen: lower case, hyphens dropped or turned into spaces.
        Assert.Equal(stored, RegistrationInviteCodes.Hash(Passphrases, minted.ToLowerInvariant()));
        Assert.Equal(stored, RegistrationInviteCodes.Hash(Passphrases, minted.Replace("-", string.Empty, StringComparison.Ordinal)));
        Assert.Equal(stored, RegistrationInviteCodes.Hash(Passphrases, " " + minted.Replace('-', ' ') + " "));
    }

    [Fact]
    public void StoresADigestNeverTheCode()
    {
        string minted = Passphrases.Generate();
        string? stored = RegistrationInviteCodes.Hash(Passphrases, minted);

        Assert.NotNull(stored);
        Assert.Equal(64, stored.Length);
        Assert.DoesNotContain(Passphrases.Normalise(minted), stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DifferentCodesNeverShareADigest()
    {
        Assert.NotEqual(
            RegistrationInviteCodes.Hash(Passphrases, "ABCD-EFGH-JKMN"),
            RegistrationInviteCodes.Hash(Passphrases, "ABCD-EFGH-JKMP"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("----")]
    [InlineData("!!! ???")]
    public void NothingOfACodeIsNoCode(string? typed)
    {
        // Null, so the caller refuses without a lookup rather than hashing "" and
        // asking the database whether the empty string is somebody's invite.
        Assert.Null(RegistrationInviteCodes.Hash(Passphrases, typed));
    }
}
