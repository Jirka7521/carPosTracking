using CarPosAPI.Services.Auth;

namespace CarPosAPI.Tests;

/// <summary>
/// Checks the properties password storage depends on. These would all still
/// "work" if the implementation were replaced with a bare SHA-256 — the login
/// page would behave identically — which is exactly why they are asserted rather
/// than assumed.
/// </summary>
public sealed class PasswordHasherTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public void AcceptsTheRightPassword()
    {
        IPasswordHasher hasher = new PasswordHasher();

        string stored = hasher.Hash(Password);

        Assert.Equal(PasswordCheckResult.Valid, hasher.Check(stored, Password));
    }

    [Fact]
    public void RejectsTheWrongPassword()
    {
        IPasswordHasher hasher = new PasswordHasher();

        string stored = hasher.Hash(Password);

        Assert.Equal(PasswordCheckResult.Failed, hasher.Check(stored, "Correct horse battery staple"));
        Assert.Equal(PasswordCheckResult.Failed, hasher.Check(stored, string.Empty));
    }

    [Fact]
    public void NeverStoresThePasswordItself()
    {
        IPasswordHasher hasher = new PasswordHasher();

        string stored = hasher.Hash(Password);

        // A hash that contains the plaintext is not a hash. Cheap to assert, and it
        // catches the "temporarily store it in plain text to debug something" change
        // that outlives its purpose.
        Assert.DoesNotContain(Password, stored, StringComparison.Ordinal);
    }

    [Fact]
    public void SaltsEveryHashSeparately()
    {
        IPasswordHasher hasher = new PasswordHasher();

        string first = hasher.Hash(Password);
        string second = hasher.Hash(Password);

        // Two users who pick the same password must not end up with the same stored
        // value — otherwise one cracked hash reveals every account that shares it,
        // and the whole table becomes a rainbow-table lookup.
        Assert.NotEqual(first, second);
        Assert.Equal(PasswordCheckResult.Valid, hasher.Check(first, Password));
        Assert.Equal(PasswordCheckResult.Valid, hasher.Check(second, Password));
    }
}
