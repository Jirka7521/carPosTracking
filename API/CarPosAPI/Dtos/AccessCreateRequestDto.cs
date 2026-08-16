using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST /api/access</c> — share a device with an existing user.
///
/// Unlike <see cref="DeviceAccessGrantInputDto"/> this names the user by id: the
/// caller has just picked them from the search results, so the id is known and
/// unambiguous (two accounts can differ only in an email the UI truncated).
/// <c>CanRead</c> is not a member — it is implied by the grant existing.
/// </summary>
/// <param name="UserId">The account to grant access to.</param>
/// <param name="DeviceId">MQTT identity of the device being shared.</param>
/// <param name="CanDelete">Whether they may soft-delete the device.</param>
/// <param name="CanShare">Whether they may re-share it; coerces <paramref name="CanModifySettings"/> on.</param>
/// <param name="CanModifySettings">Whether they may change settings and read the firmware block.</param>
public sealed record AccessCreateRequestDto(
    [Range(1, int.MaxValue)]
    int UserId,

    [Required]
    [StringLength(64, MinimumLength = 1)]
    string DeviceId,

    bool CanDelete = false,
    bool CanShare = false,
    bool CanModifySettings = false);
