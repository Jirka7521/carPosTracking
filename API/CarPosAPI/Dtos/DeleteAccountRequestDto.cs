using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>DELETE /api/me</c> — permanent account erasure.
///
/// It carries the current password because a session cookie is not enough proof
/// for something irreversible. Reading this account's data with a stolen cookie is
/// bad; being able to destroy it as well would turn the same theft into a
/// denial-of-service against the person whose data it is.
/// </summary>
/// <param name="Password">The caller's current password.</param>
public sealed record DeleteAccountRequestDto(
    [Required]
    [StringLength(256, MinimumLength = 1)]
    string Password);
