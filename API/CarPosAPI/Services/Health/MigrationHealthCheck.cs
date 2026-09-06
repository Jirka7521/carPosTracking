using CarPosAPI.Data;
using CarPosAPI.Data.SchemaSync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CarPosAPI.Services.Health;

/// <summary>
/// Reports on <c>/health</c> whether the database has every migration this build
/// expects.
///
/// <para>
/// This project never migrates a database on startup — that is a deliberate rule,
/// and the right one. The cost of it is a failure mode with no symptom: a deploy
/// whose migration step was skipped starts perfectly, passes every other check,
/// and then throws "column does not exist" on whichever endpoint touches the new
/// column first. This check is the symptom that was missing.
/// </para>
///
/// <para>
/// <b>Degraded, never Unhealthy — in both directions.</b> A pending migration
/// means some endpoints will fail, not that the process should be restarted;
/// restarting changes nothing, since nothing here applies migrations. And a check
/// that cannot answer at all (the runtime role is least-privilege and may not be
/// able to read <c>__EFMigrationsHistory</c>) must not be the thing that takes a
/// healthy container down.
/// </para>
///
/// <para>
/// Registered as a singleton so the answer can be memoised: pending migrations
/// change only when a human runs the schema-sync tool, while the container probes
/// every thirty seconds, and <c>/health</c> is required to stay cheap.
/// </para>
/// </summary>
internal sealed class MigrationHealthCheck : IHealthCheck
{
    /// <summary>
    /// How long an answer is reused. Long, because the state it describes changes
    /// only when someone deliberately migrates the database; the price is that a
    /// just-applied migration takes up to this long to show up as healthy, which
    /// is a fine trade for not querying the history table on every probe.
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>Same reasoning as <see cref="DatabaseHealthCheck"/>: stay well inside the probe's patience.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    // One refresher at a time. Without this, the first probe after the cache
    // expires could be joined by every other caller in that instant and each would
    // run its own migration query.
    private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

    private readonly IDbContextFactory<CarPosDbContext> _contextFactory;
    private readonly ILogger<MigrationHealthCheck> _logger;

    private HealthCheckResult? _cachedResult;
    private DateTime _cachedAtUtc;

    /// <summary>Creates the check.</summary>
    /// <param name="contextFactory">Opens the short-lived context the query uses.</param>
    /// <param name="logger">Receives the real failure, which the response never carries.</param>
    public MigrationHealthCheck(
        IDbContextFactory<CarPosDbContext> contextFactory,
        ILogger<MigrationHealthCheck> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>Returns the cached verdict, refreshing it when it has expired.</summary>
    /// <param name="context">Health-check context (unused).</param>
    /// <param name="cancellationToken">Cancels with the probe request.</param>
    /// <returns>Healthy when nothing is pending, Degraded otherwise.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        HealthCheckResult? cached = ReadCache();
        if (cached.HasValue)
        {
            return cached.Value;
        }

        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            // Somebody else may have refreshed it while this probe waited.
            cached = ReadCache();
            if (cached.HasValue)
            {
                return cached.Value;
            }

            HealthCheckResult result = await EvaluateAsync(cancellationToken);

            _cachedResult = result;
            _cachedAtUtc = DateTime.UtcNow;

            return result;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Returns the memoised verdict while it is still fresh.</summary>
    /// <returns>The cached result, or null when there is none or it has expired.</returns>
    private HealthCheckResult? ReadCache()
    {
        if (_cachedResult.HasValue && DateTime.UtcNow - _cachedAtUtc < CacheDuration)
        {
            return _cachedResult;
        }

        return null;
    }

    /// <summary>Asks the database which migrations it has, and compares.</summary>
    /// <param name="cancellationToken">Cancels with the probe request.</param>
    /// <returns>The verdict to cache and return.</returns>
    private async Task<HealthCheckResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource = new CancellationTokenSource(ProbeTimeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        DateTime evaluatedAtUtc = DateTime.UtcNow;

        try
        {
            await using CarPosDbContext dbContext =
                await _contextFactory.CreateDbContextAsync(linkedSource.Token);

            // The same reader the schema-sync CLI uses, so the two can never
            // disagree about what "pending" means.
            (IReadOnlyList<string> applied, IReadOnlyList<string> pending) =
                await MigrationPlanner.ReadStateAsync(dbContext, linkedSource.Token);

            Dictionary<string, object> data = new Dictionary<string, object>
            {
                ["appliedCount"] = applied.Count,
                ["pendingCount"] = pending.Count,
                // Migration ids are source artefacts, already visible in the repo —
                // naming them is what makes the report actionable.
                ["pending"] = pending,
                ["evaluatedAtUtc"] = evaluatedAtUtc,
            };

            if (pending.Count == 0)
            {
                return HealthCheckResult.Healthy("schema up to date", data);
            }

            _logger.LogWarning(
                "Database is behind the build by {PendingCount} migration(s): {PendingMigrations}",
                pending.Count,
                string.Join(", ", pending));

            return HealthCheckResult.Degraded(
                $"{pending.Count} migration(s) pending",
                data: data);
        }
        // The guard lets a cancelled probe — the caller hung up — propagate rather
        // than be cached as a verdict for the next five minutes.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Includes the timeout and the case the runtime role cannot read the
            // history table. Either way this check reports what it knows — nothing —
            // and lets the database check decide whether the connection itself is up.
            _logger.LogWarning(exception, "Health probe could not read the migration history");

            return HealthCheckResult.Degraded(
                "migration state unknown",
                data: new Dictionary<string, object>
                {
                    ["evaluatedAtUtc"] = evaluatedAtUtc,
                });
        }
    }
}
