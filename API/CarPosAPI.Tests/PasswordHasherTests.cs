using System.Buffers.Binary;
using CarPosAPI.Data.Entities;
using CarPosAPI.Services.Auth;
using Microsoft.AspNetCore.Identity;

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

    [Fact]
    public void HashesAtTheRaisedIterationCount()
    {
        IPasswordHasher hasher = new PasswordHasher();

        Assert.Equal(PasswordHasher.IterationCount, IterationCountOf(hasher.Hash(Password)));
        Assert.True(PasswordHasher.IterationCount >= 210_000);
    }

    [Fact]
    public void AnOlderHashStillVerifiesAndAsksToBeUpgraded()
    {
        // A hash written before the raise, with the framework's own default count.
        // Raising the count must not lock its owner out; it must make the next
        // sign-in rewrite it (UserAccountService handles ValidNeedsRehash).
        string legacy = new PasswordHasher<User>().HashPassword(new User(), Password);
        Assert.True(IterationCountOf(legacy) < PasswordHasher.IterationCount);

        IPasswordHasher hasher = new PasswordHasher();

        Assert.Equal(PasswordCheckResult.ValidNeedsRehash, hasher.Check(legacy, Password));
        Assert.Equal(PasswordCheckResult.Failed, hasher.Check(legacy, "wrong"));
    }

    [Fact]
    public void TheSimulatedCheckMatchesNothing()
    {
        // It exists to cost the same as a real check for an email with no account;
        // it must never be mistakable for a successful one.
        IPasswordHasher hasher = new PasswordHasher();

        Assert.Equal(PasswordCheckResult.Failed, hasher.SimulateCheck(Password));
        Assert.Equal(PasswordCheckResult.Failed, hasher.SimulateCheck(string.Empty));
    }

    /// <summary>
    /// Reads the PBKDF2 round count out of an Identity V3 hash: a format marker byte,
    /// then the PRF, the iteration count and the salt length as big-endian uint32s.
    /// </summary>
    /// <param name="hash">The base64 hash as stored.</param>
    /// <returns>The embedded iteration count.</returns>
    private static int IterationCountOf(string hash)
    {
        byte[] bytes = Convert.FromBase64String(hash);

        Assert.Equal(0x01, bytes[0]);

        return (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4));
    }
}
