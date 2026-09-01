using System.Diagnostics;
using CarPosAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Services.Health;

/// <summary>
/// Reports PostgreSQL on <c>/health</c> by actually opening a connection and
/// timing it.
///
/// <para>
/// This replaces <c>AddDbContextCheck</c>, which was fine as far as it went but
/// gave the caller nothing: no latency, no bounded wait, and — worst for an
/// unauthenticated endpoint — a description built from the raw exception, which
/// for Npgsql names the host, the database and the role. Here the failure text is
/// fixed and the detail goes to the log instead, where it belongs.
/// </para>
///
/// <para>
/// <b>Unhealthy, not Degraded.</b> Every REST endpoint and the ingest writer are
/// downstream of this connection; with it gone the process cannot do its job, and
/// answering 503 is what lets the container healthcheck notice. The MQTT and
/// scheduler checks make the opposite call for the opposite reason — see
/// <see cref="Ingest.MqttIngestHealthCheck"/>.
/// </para>
/// </summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    /// <summary>
    /// How long the probe may wait for the database. Npgsql's own connect timeout
    /// is 15 s, which is longer than the container healthcheck's 10 s patience —
    /// so a database that is merely slow to answer would otherwise be reported as
    /// a probe timeout rather than as a slow database. Five seconds leaves room
    /// for the rest of the report inside that budget.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IDbContextFactory<CarPosDbContext> _contextFactory;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    /// <summary>Creates the check.</summary>
    /// <param name="contextFactory">Opens the short-lived context the probe uses.</param>
    /// <param name="logger">Receives the real failure, which the response never carries.</param>
    public DatabaseHealthCheck(
        IDbContextFactory<CarPosDbContext> contextFactory,
        ILogger<DatabaseHealthCheck> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>Opens a connection to PostgreSQL and measures the round trip.</summary>
    /// <param name="context">Health-check context (unused).</param>
    /// <param name="cancellationToken">Cancels with the probe request.</param>
    /// <returns>Healthy with the latency, or Unhealthy with a generic reason.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutSource = new CancellationTokenSource(ProbeTimeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await using CarPosDbContext dbContext =
                await _contextFactory.CreateDbContextAsync(linkedSource.Token);

            bool reachable = await dbContext.Database.CanConnectAsync(linkedSource.Token);
            stopwatch.Stop();

            Dictionary<string, object> data = new Dictionary<string, object>
            {
                ["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                ["provider"] = dbContext.Database.ProviderName ?? "unknown",
            };

            if (!reachable)
            {
                // CanConnectAsync swallows the connection error and answers false,
                // so there is nothing to log here beyond the fact itself.
                _logger.LogWarning("Health probe could not reach the database");
                return HealthCheckResult.Unhealthy("unreachable", data: data);
            }

            return HealthCheckResult.Healthy("connection ok", data);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                "Health probe timed out waiting for the database after {TimeoutSeconds}s",
                ProbeTimeout.TotalSeconds);

            return HealthCheckResult.Unhealthy(
                $"timed out after {ProbeTimeout.TotalSeconds:0}s",
                data: new Dictionary<string, object>
                {
                    ["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                    ["timedOut"] = true,
                });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();

            // The exception goes to the log, never to the wire: its message names
            // the host, the database and the role.
            _logger.LogError(exception, "Health probe failed to reach the database");

            return HealthCheckResult.Unhealthy(
                "unreachable",
                data: new Dictionary<string, object>
                {
                    ["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                });
        }
    }
}
