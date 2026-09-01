using CarPosAPI.Services.Scheduling;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Services.Health;

/// <summary>
/// Reports the device-schedule worker on <c>/health</c>: when its last pass
/// succeeded, and whether the ones since have been failing.
///
/// <para>
/// The worker is designed never to throw and never to stop, so "is it running?"
/// cannot be answered by its absence. The only honest question is "when did it
/// last get all the way through a pass?", which is what this check asks of
/// <see cref="ScheduleWorkerState"/>.
/// </para>
///
/// <para>
/// <b>Degraded at worst.</b> Stale settings mean a tracker keeps reporting on its
/// previous interval until the next successful pass corrects it — a real problem
/// worth surfacing, but not one a container restart fixes, and not a reason to
/// take telemetry ingest and the whole REST surface down with it.
/// </para>
/// </summary>
internal sealed class ScheduleWorkerHealthCheck : IHealthCheck
{
    /// <summary>
    /// How long without a successful pass before the worker is reported stale.
    /// Four intervals, derived from the worker's own constant rather than
    /// restated, so the two cannot drift apart: it tolerates one missed tick and a
    /// slow pass without crying wolf, and still notices well inside the minutes
    /// that matter to a minute-granular schedule.
    /// </summary>
    private static readonly TimeSpan StaleAfter = DeviceConfigScheduleWorker.PassInterval * 4;

    private readonly ScheduleWorkerState _state;

    /// <summary>Creates the check.</summary>
    /// <param name="state">Shared pass-state snapshot.</param>
    public ScheduleWorkerHealthCheck(ScheduleWorkerState state)
    {
        _state = state;
    }

    /// <summary>Evaluates how recently the worker last completed a pass.</summary>
    /// <param name="context">Health-check context (unused).</param>
    /// <param name="cancellationToken">Not used — the check reads counters only.</param>
    /// <returns>Healthy when a pass completed recently, Degraded otherwise.</returns>
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
            ["devicesChangedTotal"] = _state.DevicesChangedTotal,
            ["passIntervalSeconds"] = DeviceConfigScheduleWorker.PassInterval.TotalSeconds,
        };

        if (lastPass is null)
        {
            // The startup pass runs immediately, so this is a window of a second or
            // two in practice — unless that first pass is exactly what is failing,
            // which is worth seeing. The timestamp key is left out rather than set
            // to null: the dictionary holds non-null values, and an absent key says
            // "never" just as clearly.
            return Task.FromResult(HealthCheckResult.Degraded("no pass has completed yet", data: data));
        }

        TimeSpan sinceLastPass = DateTime.UtcNow - lastPass.Value;

        data["lastSuccessfulPassAtUtc"] = lastPass.Value;
        data["secondsSinceLastPass"] = Math.Round(sinceLastPass.TotalSeconds, 1);

        if (sinceLastPass > StaleAfter)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"no successful pass in {sinceLastPass.TotalSeconds:0}s; the fleet may be running stale settings",
                data: data));
        }

        if (consecutiveFailures > 0)
        {
            // Recent enough not to be stale yet, but the passes since have thrown —
            // the state it is in on the way to stale, and the earliest warning there is.
            return Task.FromResult(HealthCheckResult.Degraded(
                $"{consecutiveFailures} pass(es) failed since the last success",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("reconciling on schedule", data));
    }
}
