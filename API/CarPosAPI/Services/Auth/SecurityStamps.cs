namespace CarPosAPI.Services.Auth;

/// <summary>
/// Makes the values stored in <see cref="Data.Entities.User.SecurityStamp"/>.
/// </summary>
internal static class SecurityStamps
{
    /// <summary>A fresh, unpredictable stamp: 32 lower-case hex characters.</summary>
    /// <returns>The new stamp.</returns>
    public static string New()
    {
        // Guid.NewGuid is backed by the OS CSPRNG. Unpredictability is not even
        // load-bearing here — the stamp only ever travels inside a signed token —
        // but uniqueness across rotations is.
        return Guid.NewGuid().ToString("N");
    }
}
