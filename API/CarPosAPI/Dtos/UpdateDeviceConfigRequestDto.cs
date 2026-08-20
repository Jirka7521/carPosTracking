using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// A full replacement of a device's settings — not a patch. Every field is required,
/// so a client that forgets one is told (400) rather than silently keeping a value it
/// did not mean to keep. Saving inserts a new revision; it never edits an old one.
///
/// <para>
/// The <c>[Range]</c> bounds come from <see cref="DeviceConfigRules"/> and mirror the
/// firmware's clamps exactly. Rejecting here rather than clamping is deliberate: a
/// person at a dashboard can be shown what is wrong, whereas the device — which has
/// nobody to ask — clamps and carries on.
/// </para>
/// </summary>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">
/// Deep-sleep and power the modem down between reports. Saves a great deal of battery
/// above a few minutes, at the cost of a cold GNSS fix and a fresh TLS handshake every
/// cycle — validation cannot express that trade-off, so the UI explains it instead.
/// </param>
/// <param name="FixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle.</param>
/// <param name="QueueMaxFixes">How many undelivered fixes the SD queue may hold.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a fix this API rejected.</param>
/// <param name="RetryMaxAgeHours">Hours after which a still-rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">
/// How often an awake device asks the broker to re-send this document. A backstop
/// only — a saved change normally reaches the device by push within a second. It has
/// no effect at all while <paramref name="SleepBetween"/> is on, because a sleeping
/// device re-reads its configuration on every wake.
/// </param>
public sealed record UpdateDeviceConfigRequestDto(
    [Required]
    [Range(DeviceConfigRules.MinIntervalSeconds, DeviceConfigRules.MaxIntervalSeconds)]
    int IntervalSeconds,

    [Required]
    bool SleepBetween,

    [Required]
    [Range(DeviceConfigRules.MinFixTimeoutSeconds, DeviceConfigRules.MaxFixTimeoutSeconds)]
    int FixTimeoutSeconds,

    [Required]
    [Range(DeviceConfigRules.MinQueueMaxFixes, DeviceConfigRules.MaxQueueMaxFixes)]
    int QueueMaxFixes,

    [Required]
    [Range(DeviceConfigRules.MinRetryIntervalHours, DeviceConfigRules.MaxRetryIntervalHours)]
    int RetryIntervalHours,

    [Required]
    [Range(DeviceConfigRules.MinRetryMaxAgeHours, DeviceConfigRules.MaxRetryMaxAgeHours)]
    int RetryMaxAgeHours,

    [Required]
    [Range(DeviceConfigRules.MinConfigCheckSeconds, DeviceConfigRules.MaxConfigCheckSeconds)]
    int ConfigCheckSeconds);
