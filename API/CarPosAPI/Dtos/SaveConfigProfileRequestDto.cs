using System.ComponentModel.DataAnnotations;

namespace CarPosAPI.Dtos;

/// <summary>
/// Creates a profile, or replaces one wholesale. Every field is required, for the same
/// reason as <see cref="UpdateDeviceConfigRequestDto"/>: a client that forgets one is
/// told, rather than silently keeping a value nobody chose.
///
/// <para>
/// The <c>[Range]</c> bounds are the same <see cref="DeviceConfigRules"/> constants the
/// manual settings form validates against — a profile is not a lesser kind of
/// configuration, and a value the API would reject on the settings panel must not
/// become reachable by routing it through a schedule.
/// </para>
///
/// <para>
/// One DTO for both create and update because the operations differ only in whether a
/// row already exists. Two near-identical records would be two places to add the next
/// setting to, and one of them would be missed.
/// </para>
/// </summary>
/// <param name="Name">What to call it. Unique per device, case-insensitively.</param>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep and power the modem down between reports.</param>
/// <param name="FixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle.</param>
/// <param name="QueueMaxFixes">How many undelivered fixes the SD queue may hold.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a fix this API rejected.</param>
/// <param name="RetryMaxAgeHours">Hours after which a still-rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">How often an awake device asks the broker to re-send its configuration.</param>
public sealed record SaveConfigProfileRequestDto(
    [Required]
    [StringLength(
        ScheduleRules.MaxProfileNameLength,
        MinimumLength = ScheduleRules.MinProfileNameLength)]
    string Name,

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
