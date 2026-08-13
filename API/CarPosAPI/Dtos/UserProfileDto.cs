namespace CarPosAPI.Dtos;

/// <summary>
/// A user as every endpoint exposes them: identity and names, never credentials.
///
/// SECURITY: there is deliberately no password-hash member here and there must
/// never be one. This shape is returned to *other* users too — the sharing picker
/// (<c>GET /api/users?email=</c>) hands it to anyone looking for someone to share
/// a device with — so it must contain nothing beyond what a colleague may see.
/// </summary>
/// <param name="Id">Surrogate key; the value the sharing endpoints reference.</param>
/// <param name="Email">Login identity, normalised to lower case.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
public sealed record UserProfileDto(
    int Id,
    string Email,
    string FirstName,
    string LastName);
