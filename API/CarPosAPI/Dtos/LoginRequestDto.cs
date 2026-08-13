using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST /api/auth/login</c>.
///
/// The validation here is deliberately looser than
/// <see cref="RegisterRequestDto"/>'s: a sign-in attempt with a 4-character
/// password must fail as "invalid credentials", not as a 400 that tells the
/// caller the password policy — and existing accounts must keep working if that
/// policy is ever tightened.
/// </summary>
/// <param name="Email">Login identity; compared case-insensitively.</param>
/// <param name="Password">The password to verify. Never logged.</param>
public sealed record LoginRequestDto(
    [Required]
    [StringLength(256)]
    string Email,

    [Required]
    [StringLength(256)]
    string Password);
