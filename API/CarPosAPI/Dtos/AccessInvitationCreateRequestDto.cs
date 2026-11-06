using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST /api/access/invitations</c>: make a single-use link that
/// lets whoever accepts it share this device, with these capabilities.
///
/// There is no recipient in it, on purpose. The link is not addressed to anybody —
/// the sharer hands it over however they like — so making one reveals nothing about
/// which people have an account here.
/// </summary>
/// <param name="DeviceId">The device's MQTT identity.</param>
/// <param name="CanDelete">Whether the accepted grant may delete the device (owner-only to give).</param>
/// <param name="CanShare">Whether it may share the device in turn. Implies <paramref name="CanModifySettings"/>.</param>
/// <param name="CanModifySettings">Whether it may change the device's settings.</param>
public sealed record AccessInvitationCreateRequestDto(
    [Required]
    [StringLength(64, MinimumLength = 1)]
    string DeviceId,

    bool CanDelete = false,
    bool CanShare = false,
    bool CanModifySettings = false);
