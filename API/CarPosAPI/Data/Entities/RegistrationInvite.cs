namespace CarPosAPI.Data.Entities;

/// <summary>
/// One single-use invite code for <c>POST /api/auth/register</c>, minted by the
/// operator with the <c>create-registration-invite</c> CLI mode.
///
/// <para>
/// Only the code's digest is stored (<see cref="CodeHash"/>), never the code: a copy of
/// this table hands out no way in. And it records no user — not who the code was for,
/// not who redeemed it — so the table carries no personal data, needs no export or
/// erasure path, and says nothing about anyone if it leaks.
/// </para>
/// Mapped by <see cref="Configurations.RegistrationInviteConfiguration"/>.
/// </summary>
public sealed class RegistrationInvite
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Lower-case hex SHA-256 of the normalised code (see
    /// <c>Services/Auth/RegistrationInviteCodes</c>). Unique.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>When the code was minted (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>After this instant (UTC) the code no longer opens registration.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the code was used (UTC); null while it is still unused. A code works once.</summary>
    public DateTime? UsedAt { get; set; }
}
