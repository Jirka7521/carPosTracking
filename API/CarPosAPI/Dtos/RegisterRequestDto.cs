using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST /api/auth/register</c>. Validated by
/// <c>[ApiController]</c> before the action runs.
/// </summary>
/// <param name="Email">
/// Login identity. Stored lower-cased, so registering "A@b.cz" and later signing
/// in as "a@b.cz" is the same account.
/// </param>
/// <param name="Password">
/// The chosen password. Only a length floor is enforced: composition rules
/// ("one digit, one symbol") push people towards shorter, more predictable
/// passwords, whereas length is what actually costs an attacker.
/// </param>
/// <param name="FirstName">Given name shown in the UI.</param>
/// <param name="LastName">Family name shown in the UI.</param>
public sealed record RegisterRequestDto(
    [Required]
    [EmailAddress]
    [StringLength(256, MinimumLength = 3)]
    string Email,

    // 12 characters, matching the minimum the frontend advertises on the
    // registration form. The upper bound is not a policy but a denial-of-service
    // guard: PBKDF2 hashes whatever it is given, so an unbounded password is an
    // unbounded amount of server CPU per login attempt.
    [Required]
    [StringLength(256, MinimumLength = 12)]
    string Password,

    [Required]
    [StringLength(128, MinimumLength = 1)]
    string FirstName,

    [Required]
    [StringLength(128, MinimumLength = 1)]
    string LastName);
