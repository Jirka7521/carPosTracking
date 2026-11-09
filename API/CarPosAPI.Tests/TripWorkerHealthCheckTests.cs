using CarPosAPI.Services.Health;
using CarPosAPI.Services.Trips;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="TripWorkerHealthCheck"/>. Unlike the schedule worker's check there is
/// deliberately no staleness rule — the trip worker runs only when positions are saved —
/// so the tests pin that a long-quiet but healthy worker stays Healthy.
/// </summary>
public sealed class TripWorkerHealthCheckTests
{
    private static readonly HealthCheckContext Context = new HealthCheckContext();

    [Fact]
    public async Task BeforeTheFirstPass_IsDegraded()
    {
        HealthCheckResult result = await new TripWorkerHealthCheck(new TripWorkerState()).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task AQuietButSuccessfulWorker_StaysHealthy()
    {
        TripWorkerState state = new TripWorkerState();

        // A fleet parked for a day: the last pass was long ago, and that is fine.
        state.RecordPassSucceeded(tripsChanged: 3, passAtUtc: DateTime.UtcNow.AddDays(-1));

        HealthCheckResult result = await new TripWorkerHealthCheck(state).CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(3L, result.Data["tripsChangedTotal"]);
    }

    [Fact]
    public async Task FailingPasses_AreDegraded_UntilOneSucceeds()
    {
        TripWorkerState state = new TripWorkerState();
        state.RecordPassSucceeded(tripsChanged: 0, passAtUtc: DateTime.UtcNow);
        state.RecordPassFailed();
        state.RecordPassFailed();

        HealthCheckResult failing = await new TripWorkerHealthCheck(state).CheckHealthAsync(Context);
        Assert.Equal(HealthStatus.Degraded, failing.Status);
        Assert.Equal(2L, failing.Data["consecutiveFailures"]);

        state.RecordPassSucceeded(tripsChanged: 0, passAtUtc: DateTime.UtcNow);
        HealthCheckResult recovered = await new TripWorkerHealthCheck(state).CheckHealthAsync(Context);
        Assert.Equal(HealthStatus.Healthy, recovered.Status);
    }
}
