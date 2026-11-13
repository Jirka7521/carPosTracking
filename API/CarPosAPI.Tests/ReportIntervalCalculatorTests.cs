using CarPosAPI.Services.Devices;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="ReportIntervalCalculator"/>: which sets of settings decide how long
/// a device may stay quiet, and that the longest of them wins.
///
/// <para>
/// The cases that matter are the ones where picking the wrong set paints a pin red
/// that should be blue: a schedule that has just switched to a shorter plan, a change
/// the sleeping device has not picked up yet, and a moving interval set longer than
/// the standby one.
/// </para>
/// </summary>
public sealed class ReportIntervalCalculatorTests
{
    // Firmware defaults: 20 min parked, 30 s while moving.
    private static readonly ReportIntervalCandidate Defaults = new ReportIntervalCandidate(1200, true, 30);

    private static readonly ReportIntervalCandidate NightPlan = new ReportIntervalCandidate(21600, true, 30);
    private static readonly ReportIntervalCandidate DayPlan = new ReportIntervalCandidate(600, true, 30);

    private readonly ReportIntervalCalculator _calculator = new ReportIntervalCalculator();

    [Fact]
    public void NoSchedule_UsesTheStandbyInterval()
    {
        int? result = _calculator.MaxIntervalSeconds(
            scheduleInForce: false,
            activeProfile: null,
            reportedProfile: null,
            desired: Defaults,
            applied: Defaults);

        Assert.Equal(1200, result);
    }

    [Fact]
    public void MovingIntervalLongerThanStandby_WinsOnlyWhileMotionIsOn()
    {
        ReportIntervalCandidate withMotion = new ReportIntervalCandidate(300, true, 900);
        ReportIntervalCandidate withoutMotion = new ReportIntervalCandidate(300, false, 900);

        Assert.Equal(900, _calculator.MaxIntervalSeconds(false, null, null, withMotion, null));

        // With motion wake off the moving set never takes over, so its interval is moot.
        Assert.Equal(300, _calculator.MaxIntervalSeconds(false, null, null, withoutMotion, null));
    }

    [Fact]
    public void PendingChange_TakesTheLongerOfDesiredAndApplied()
    {
        // Retuned from 6 h to 10 min while the device sleeps: it keeps the 6 h timer
        // until it wakes, so the 6 h one still decides.
        int? result = _calculator.MaxIntervalSeconds(false, null, null, desired: DayPlan, applied: NightPlan);

        Assert.Equal(21600, result);
    }

    [Fact]
    public void Schedule_TakesTheLongerOfTheActiveAndTheReportedPlan()
    {
        // Just switched from night to day: the latest fix was taken on the night plan.
        int? result = _calculator.MaxIntervalSeconds(
            scheduleInForce: true,
            activeProfile: DayPlan,
            reportedProfile: NightPlan,
            desired: Defaults,
            applied: Defaults);

        Assert.Equal(21600, result);
    }

    [Fact]
    public void Schedule_IgnoresTheConfigDocumentWhileItResolves()
    {
        ReportIntervalCandidate longDocument = new ReportIntervalCandidate(86400, false, 30);

        int? result = _calculator.MaxIntervalSeconds(true, DayPlan, DayPlan, longDocument, longDocument);

        Assert.Equal(600, result);
    }

    [Fact]
    public void ScheduleResolvingToNothing_FallsBackToTheConfigDocument()
    {
        int? result = _calculator.MaxIntervalSeconds(true, null, null, Defaults, null);

        Assert.Equal(1200, result);
    }

    [Fact]
    public void OverrideLive_UsesTheConfigDocumentNotThePlans()
    {
        // The service passes scheduleInForce: false while an override holds.
        int? result = _calculator.MaxIntervalSeconds(false, NightPlan, NightPlan, DayPlan, DayPlan);

        Assert.Equal(600, result);
    }

    [Fact]
    public void NothingKnown_IsNull()
    {
        Assert.Null(_calculator.MaxIntervalSeconds(false, null, null, null, null));
        Assert.Null(_calculator.OverdueAfterSeconds(null));
    }

    [Fact]
    public void OverdueAfter_IsTwoIntervals()
    {
        Assert.Equal(2400, _calculator.OverdueAfterSeconds(1200));
    }
}
