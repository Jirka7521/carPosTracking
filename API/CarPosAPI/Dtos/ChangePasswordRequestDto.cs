using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>PUT /api/users/{id}/password</c>.
///
/// <paramref name="CurrentPassword"/> is required as proof of identity, not as
/// ceremony: without it, a stolen session cookie could be turned into permanent
/// account takeover by simply changing the password and locking the real owner
/// out.
/// </summary>
/// <param name="CurrentPassword">The password in force right now. Never logged.</param>
/// <param name="NewPassword">The replacement; same length floor as registration.</param>
public sealed record ChangePasswordRequestDto(
    [Required]
    [StringLength(256)]
    string CurrentPassword,

    [Required]
    [StringLength(256, MinimumLength = 12)]
    string NewPassword);
