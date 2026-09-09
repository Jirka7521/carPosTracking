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

    /// <summary>
    /// Version of the terms of use and privacy policy this account accepted at
    /// registration. One acceptance, one version, both documents.
    ///
    /// This is what makes the terms binding rather than merely published. The
    /// PolyForm licence in the repository reaches people who copy the source, not
    /// people who register here, so without a recorded acceptance the no-warranty and
    /// no-liability sections would bind nobody — and the Art. 6(1)(b) contract the
    /// processing relies on would have nothing to point at. Storing the version the
    /// user was actually shown, next to the moment they accepted it, is what makes
    /// that answerable a year later.
    ///
    /// The column name is historical: it predates the terms being folded into the
    /// same acceptance, and renaming it would churn the entity, the configuration,
    /// the export, two migrations and the model snapshot for nothing.
    ///
    /// Empty on rows that predate the field; registration refuses to create a new
    /// account without a version matching <c>PrivacyOptions.PolicyVersion</c>.
    /// </summary>
    public string PrivacyPolicyVersion { get; set; } = string.Empty;

    /// <summary>
    /// When <see cref="PrivacyPolicyVersion"/> was accepted (UTC). Null for accounts
    /// created before acknowledgement was recorded.
    /// </summary>
    public DateTime? PrivacyPolicyAcceptedAt { get; set; }
}
