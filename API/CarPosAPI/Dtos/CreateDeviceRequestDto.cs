using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Request body of <c>POST /api/devices</c>. Carries everything the caller gets
/// to choose; the RSA key pair is generated server-side and is never accepted
/// from a client. Validated by <c>[ApiController]</c> before the action runs, so
/// the service layer can assume a well-formed device id.
/// </summary>
/// <param name="DeviceId">
/// The device's MQTT identity, e.g. <c>GNSS01</c>. Case is preserved exactly as
/// sent — MQTT topics are case-sensitive (see <see cref="Data.Entities.Device.DeviceId"/>).
/// </param>
/// <param name="DisplayName">Optional human-friendly name shown in the dashboard.</param>
/// <param name="AdditionalAccesses">
/// Optional people to share the new device with immediately. The creator always
/// receives all four capabilities regardless of what appears here — that grant is
/// added by the service, never taken from the request.
/// </param>
public sealed record CreateDeviceRequestDto(
    // The character class is a security control, not cosmetics: this value is
    // interpolated into MQTT topics (devices/<id>, devices/<id>/config), so the
    // separator and both wildcards must be impossible to smuggle in. The 64-char
    // ceiling matches the device_id column in DeviceConfiguration.
    //
    // Together these spell the same shape as the ingest topic guard's
    // DeviceIdRegex ("^[A-Za-z0-9_-]{1,64}$" in IngestPipeline). Keep them in
    // step: anything accepted here but rejected there would provision a device
    // whose fixes are silently dropped on arrival.
    [Required]
    [StringLength(64, MinimumLength = 1)]
    [RegularExpression(
        "^[A-Za-z0-9_-]+$",
        ErrorMessage = "DeviceId may contain only letters, digits, hyphens and underscores.")]
    string DeviceId,
    [StringLength(128)]
    string? DisplayName = null,

    // The cap is a guard, not a business rule: each entry costs an email lookup
    // and an INSERT inside the provisioning transaction, so an unbounded list
    // would let one request hold a write transaction open indefinitely.
    [MaxLength(32)]
    IReadOnlyList<DeviceAccessGrantInputDto>? AdditionalAccesses = null);
