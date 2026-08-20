namespace CarPosAPI.Dtos;

/// <summary>
/// The six remote settings, with no version or authorship around them.
///
/// Factored out because the same six values appear in three places — the revision
/// currently published, the (possibly older) revision a device is actually running,
/// and every entry in the history list — and the dashboard diffs them against each
/// other field by field. One shape means the diff is written once.
/// </summary>
/// <param name="IntervalSeconds">Seconds between position reports.</param>
/// <param name="SleepBetween">Deep-sleep and power the modem down between reports.</param>
/// <param name="FixTimeoutSeconds">How long to chase a GNSS lock before giving up on a cycle.</param>
/// <param name="QueueMaxFixes">How many undelivered fixes the SD queue may hold.</param>
/// <param name="RetryIntervalHours">Hours between attempts on a rejected fix.</param>
/// <param name="RetryMaxAgeHours">Hours after which a still-rejected fix is abandoned; 0 = never.</param>
/// <param name="ConfigCheckSeconds">How often an awake device re-asks the broker for this document.</param>
public sealed record DeviceConfigValuesDto(
    int IntervalSeconds,
    bool SleepBetween,
    int FixTimeoutSeconds,
    int QueueMaxFixes,
    int RetryIntervalHours,
    int RetryMaxAgeHours,
    int ConfigCheckSeconds);
