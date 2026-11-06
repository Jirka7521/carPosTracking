using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST /api/access/invitations/preview</c>, <c>/accept</c> and
/// <c>/decline</c>. The token travels in the body rather than the URL so it never
/// reaches an access log, exactly as share-link redemption does.
/// </summary>
/// <param name="Token">The invitation link's token, <c>selector.verifier</c>.</param>
public sealed record AccessInvitationTokenRequestDto(
    // A real token is 66 characters; the ceiling only stops an oversized body from
    // being parsed at all.
    [Required]
    [StringLength(128, MinimumLength = 1)]
    string Token);
