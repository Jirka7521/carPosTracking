using CarPosAPI.Services.Health;
using CarPosAPI.Services.Scheduling;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="ScheduleWorkerHealthCheck"/> — the one place a schedule
/// worker that has quietly stopped reconciling becomes visible.
///
/// <para>
/// The staleness threshold is testable only because
/// <see cref="ScheduleWorkerState.RecordPassSucceeded"/> takes the pass instant
/// rather than reading a clock: a pass can be recorded as having happened ten
/// minutes ago without anyone waiting ten minutes.
/// </para>
/// </summary>
public sealed class ScheduleWorkerHealthCheckTests
{
    private static readonly HealthCheckContext Context = new HealthCheckContext();

    [Fact]
    public async Task BeforeTheFirstPass_IsDegraded()
    {
        ScheduleWorkerState state = new ScheduleWorkerState();

        HealthCheckResult result = await new ScheduleWorkerHealthCheck(state).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.False(result.Data.ContainsKey("lastSuccessfulPassAtUtc"));
    }

    [Fact]
    public async Task AfterARecentPass_IsHealthy()
    {
        ScheduleWorkerState state = new ScheduleWorkerState();
        state.RecordPassSucceeded(devicesChanged: 2, passAtUtc: DateTime.UtcNow);

        HealthCheckResult result = await new ScheduleWorkerHealthCheck(state).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1L, result.Data["passesCompleted"]);
        Assert.Equal(2L, result.Data["devicesChangedTotal"]);
        Assert.Equal(0L, result.Data["consecutiveFailures"]);
    }

    [Fact]
    public async Task WhenTheLastSuccessIsOlderThanTheStaleWindow_IsDegraded()
    {
        ScheduleWorkerState state = new ScheduleWorkerState();

        // Four pass intervals is the threshold; ten minutes is well past it.
        state.RecordPassSucceeded(devicesChanged: 0, passAtUtc: DateTime.UtcNow.AddMinutes(-10));

        HealthCheckResult result = await new ScheduleWorkerHealthCheck(state).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("stale settings", result.Description);
    }

    [Fact]
    public async Task FailuresSinceTheLastSuccess_AreDegradedBeforeTheWindowExpires()
    {
        ScheduleWorkerState state = new ScheduleWorkerState();
        state.RecordPassSucceeded(devicesChanged: 0, passAtUtc: DateTime.UtcNow);
        state.RecordPassFailed();

        HealthCheckResult result = await new ScheduleWorkerHealthCheck(state).CheckHealthAsync(Context);

        // The earliest warning there is: the last success is still recent, but the
        // passes since have been throwing.
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(1L, result.Data["consecutiveFailures"]);
        Assert.Equal(1L, result.Data["passesFailed"]);
    }

    [Fact]
    public async Task ASuccessClearsTheFailureStreak()
    {
        ScheduleWorkerState state = new ScheduleWorkerState();
        state.RecordPassFailed();
        state.RecordPassFailed();
        state.RecordPassSucceeded(devicesChanged: 0, passAtUtc: DateTime.UtcNow);

        HealthCheckResult result = await new ScheduleWorkerHealthCheck(state).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0L, result.Data["consecutiveFailures"]);
        // The total is a different question and must survive the reset.
        Assert.Equal(2L, result.Data["passesFailed"]);
    }
}
