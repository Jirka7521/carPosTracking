using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>PUT /api/users/{id}</c> — a partial update of the caller's
/// own names. Both members are optional: a null field means "leave unchanged",
/// which is why neither is <c>[Required]</c>. The email is not updatable here
/// because it is the login identity; changing it needs a verification flow that
/// does not exist yet.
/// </summary>
/// <param name="FirstName">New given name, or null to keep the current one.</param>
/// <param name="LastName">New family name, or null to keep the current one.</param>
public sealed record UserUpdateRequestDto(
    [StringLength(128, MinimumLength = 1)]
    string? FirstName = null,

    [StringLength(128, MinimumLength = 1)]
    string? LastName = null);
