namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// The clock behind schedules: wakes on a fixed interval and asks
/// <see cref="IScheduleReconciler"/> to bring the fleet into line.
///
/// <para>
/// <b>Thirty seconds, and no cleverness about it.</b> Windows are minute-granular, so
/// this is at most thirty seconds late — well inside the noise of a tracker that reports
/// once a minute and applies a change on its next wake. The obvious optimisation, to
/// sleep until the next boundary, would need the worker to know every device's next
/// boundary, recompute that whenever anyone edited a rule, and get the wake-up right
/// across a restart. It would buy nothing a person could perceive and cost the one
/// property that makes this component boring: a pass either happens or the next one
/// does.
/// </para>
///
/// <para>
/// <b>Nothing here throws.</b> A failed pass is logged and the next one is attempted;
/// a database that is down must not take the process with it, for the same reason
/// <see cref="Ingest.MqttConfigPublisher"/> swallows a broker fault — telemetry
/// ingest and the whole REST surface are in this process too, and stale settings are a
/// far smaller problem than an API that will not start.
/// </para>
///
/// <para>
/// A scope per pass, because the reconciler and the revision writer are scoped: a
/// singleton holding a <c>DbContext</c> for the process's lifetime would be the captive
/// dependency this project's rules single out, and its change tracker would grow for
/// ever.
/// </para>
/// </summary>
internal sealed class DeviceConfigScheduleWorker : BackgroundService
{
    /// <summary>
    /// How often the fleet is reconciled. See the class summary for why this is a plain
    /// interval rather than a computed sleep.
    /// </summary>
    private static readonly TimeSpan PassInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceConfigScheduleWorker> _logger;

    /// <summary>Creates the worker.</summary>
    /// <param name="scopeFactory">Opens a DI scope per pass.</param>
    /// <param name="logger">Structured logger.</param>
    public DeviceConfigScheduleWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DeviceConfigScheduleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Runs one pass immediately, then one every <see cref="PassInterval"/> until the
    /// host stops.
    /// </summary>
    /// <param name="stoppingToken">Signalled on shutdown.</param>
    /// <returns>A task that completes when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Device schedule worker started; reconciling every {IntervalSeconds}s",
            PassInterval.TotalSeconds);

        // The startup pass matters more than the periodic ones: it is what puts a fleet
        // back in step after a deployment that spanned a boundary. Everything a restart
        // could have missed is corrected here, without anyone having to notice.
        await RunPassAsync(stoppingToken);

        using PeriodicTimer timer = new PeriodicTimer(PassInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunPassAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown. Nothing to report.
        }

        _logger.LogInformation("Device schedule worker stopped");
    }

    /// <summary>
    /// Runs one reconciling pass in its own scope, converting any failure into a log
    /// line rather than an unhandled exception on a background thread.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the pass is done or has failed.</returns>
    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IScheduleReconciler reconciler =
                scope.ServiceProvider.GetRequiredService<IScheduleReconciler>();

            int changed = await reconciler.ReconcileAsync(DateTime.UtcNow, cancellationToken);

            // Only when something happened. A pass that finds a correct fleet — which is
            // almost all of them — must be silent, or the log becomes a metronome and
            // the entries that matter are lost in it.
            if (changed > 0)
            {
                _logger.LogInformation(
                    "Schedule pass changed the settings of {ChangedCount} device(s)",
                    changed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-pass. The next start's pass picks up whatever was missed.
        }
        catch (Exception exception)
        {
            // Deliberately broad — see the class summary. A transient database or broker
            // fault must cost one pass, not the process.
            _logger.LogWarning(
                exception,
                "Schedule pass failed; the fleet may be running stale settings until the next one");
        }
    }
}
