namespace CarPosAPI.Data.Entities;

/// <summary>
/// An account that signs in to the dashboard. Users own nothing directly — what
/// they may see or do is decided entirely by their <see cref="Access"/> rows, so
/// this entity carries identity and credentials only. Mapped by
/// <see cref="Configurations.UserConfiguration"/>.
/// </summary>
public sealed class User
{
    /// <summary>Surrogate key (int identity). Travels in the JWT <c>sub</c> claim.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Login identity, stored lower-cased so "A@b.cz" and "a@b.cz" can never
    /// become two accounts. The unique index is on this normalised value, which is
    /// also what the sharing lookup (<c>GET /api/users?email=</c>) matches against.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2 hash produced by ASP.NET Core's <c>PasswordHasher&lt;User&gt;</c>
    /// (salt and iteration count are embedded in the string).
    /// SECRET: never select it into a DTO and never log it.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Given name shown in the UI header and the sharing list.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Family name shown alongside <see cref="FirstName"/>.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Account creation timestamp (UTC). DB-generated default.</summary>
    public DateTime CreatedAt { get; set; }
}
