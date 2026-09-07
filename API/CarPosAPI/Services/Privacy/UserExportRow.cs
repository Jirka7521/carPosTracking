namespace CarPosAPI.Services.Privacy;

/// <summary>
/// The account fields that appear in a data export. A dedicated projection target
/// rather than the <see cref="Data.Entities.User"/> entity, because the entity
/// carries <c>PasswordHash</c> and the export must not be one careless
/// <c>Include</c> away from handing it out.
/// </summary>
/// <param name="Id">Surrogate key.</param>
/// <param name="Email">Login identity.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="CreatedAt">When the account was created (UTC).</param>
/// <param name="PrivacyPolicyVersion">Policy version acknowledged at registration.</param>
/// <param name="PrivacyPolicyAcceptedAt">When it was acknowledged (UTC), if ever.</param>
public sealed record UserExportRow(
    int Id,
    string Email,
    string FirstName,
    string LastName,
    DateTime CreatedAt,
    string PrivacyPolicyVersion,
    DateTime? PrivacyPolicyAcceptedAt);
