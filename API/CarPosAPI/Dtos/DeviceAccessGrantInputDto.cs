using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// One "share it with them too" entry inside <see cref="CreateDeviceRequestDto"/>.
///
/// Users are named by <em>email</em> rather than id on purpose: at the moment
/// someone registers a device they know their colleague's address, not their
/// database key. An address that matches no account is skipped silently — the
/// alternative, reporting whether an email exists, turns this endpoint into an
/// account-enumeration oracle.
///
/// There is no <c>CanRead</c> member: read access is implied by the grant
/// existing at all, so offering it as a flag would only allow the nonsensical
/// "access without access".
/// </summary>
/// <param name="UserEmail">Address of the account to share with.</param>
/// <param name="CanDelete">Whether they may soft-delete the device.</param>
/// <param name="CanShare">Whether they may re-share it; coerces <paramref name="CanModifySettings"/> on.</param>
/// <param name="CanModifySettings">Whether they may change settings and read the firmware block.</param>
public sealed record DeviceAccessGrantInputDto(
    [Required]
    [EmailAddress]
    [StringLength(256)]
    string UserEmail,

    bool CanDelete = false,
    bool CanShare = false,
    bool CanModifySettings = false);
