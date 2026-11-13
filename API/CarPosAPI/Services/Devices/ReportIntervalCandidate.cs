namespace CarPosAPI.Services.Devices;

/// <summary>
/// The three settings that decide how long a device may stay quiet between reports,
/// taken from one set of values that might be in force — a config revision or a
/// schedule profile.
///
/// <para>
/// A separate, EF-free shape so <see cref="ReportIntervalCalculator"/> can be tested
/// with plain records, the same reason <c>ScheduleRuleSnapshot</c> exists for the
/// schedule evaluator.
/// </para>
/// </summary>
/// <param name="IntervalSeconds">The standby (parked) reporting interval.</param>
/// <param name="MotionEnabled">Whether the moving set can take over while driving.</param>
/// <param name="MovingIntervalSeconds">The interval while moving; ignored when motion is off.</param>
internal sealed record ReportIntervalCandidate(
    int IntervalSeconds,
    bool MotionEnabled,
    int MovingIntervalSeconds);
