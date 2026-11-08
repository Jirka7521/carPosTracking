using CarPosAPI.Services.Trips;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Services.Health;

/// <summary>
/// Reports the trip detection worker on <c>/health</c>: whether its last pass succeeded.
///
/// <para>
/// <b>No staleness rule</b>, unlike <see cref="ScheduleWorkerHealthCheck"/>: this worker
/// runs only when positions are saved, so a parked car legitimately triggers a pass every
/// twenty minutes and a switched-off fleet none at all. Silence is not a symptom here;
/// failing passes are.
/// </para>
///
/// <para>
/// <b>Degraded at worst.</b> Trips lagging behind positions is worth surfacing but is no
/// reason to take ingest and the REST API down with it.
/// </para>
/// </summary>
internal sealed class TripWorkerHealthCheck : IHealthCheck
{
    private readonly TripWorkerState _state;

    /// <summary>Creates the check.</summary>
    /// <param name="state">Shared pass-state snapshot.</param>
    public TripWorkerHealthCheck(TripWorkerState state)
    {
        _state = state;
    }

    /// <summary>Evaluates the worker's recent passes.</summary>
    /// <param name="context">Health-check context (unused).</param>
    /// <param name="cancellationToken">Not used — the check reads counters only.</param>
    /// <returns>Healthy when the last pass succeeded, Degraded otherwise.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        DateTime? lastPass = _state.LastSuccessfulPassAtUtc;
        long consecutiveFailures = _state.ConsecutiveFailures;

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            ["passesCompleted"] = _state.PassesCompleted,
            ["passesFailed"] = _state.PassesFailed,
            ["consecutiveFailures"] = consecutiveFailures,
            ["tripsChangedTotal"] = _state.TripsChangedTotal,
        };

        if (lastPass is not null)
        {
            data["lastSuccessfulPassAtUtc"] = lastPass.Value;
        }

        if (consecutiveFailures > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"{consecutiveFailures} trip pass(es) failed since the last success; trips may lag behind positions",
                data: data));
        }

        if (lastPass is null)
        {
            // The startup pass runs straight away, so this lasts moments — unless the
            // first pass is what is failing, which the branch above reports.
            return Task.FromResult(HealthCheckResult.Degraded("no trip pass has completed yet", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("trips follow saved positions", data));
    }
}
