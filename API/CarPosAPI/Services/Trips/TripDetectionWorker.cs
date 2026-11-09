namespace CarPosAPI.Services.Trips;

/// <summary>
/// Keeps trip starts in step with positions — <b>driven by saves, not a clock</b>.
///
/// <para>
/// It waits on <see cref="ITripDetectionSignal"/>, which ingest raises after every insert,
/// and runs a pass the moment it is woken. When no data arrives nothing runs. The only
/// passes not caused by a save are: one at startup (catching up on anything saved while
/// the API was down — and, on the very first start, backfilling the whole history), an
/// immediate follow-up while a pass reports work left over its budget, and a retry with
/// backoff after a failure.
/// </para>
///
/// <para>
/// <b>Not inline in the INSERT, deliberately.</b> Ingest handles MQTT messages strictly
/// one at a time, so detection there would hold every message up behind it; a detection
/// bug — or the trip migration not yet applied — must never cost a stored position; and
/// late data can mean recomputing a whole window, not just looking at one fix. Running a
/// few milliseconds after the save, off the ingest path, gives the same result without
/// any of those risks.
/// </para>
///
/// <para>
/// <b>Nothing here throws.</b> Like <c>DeviceConfigScheduleWorker</c>: a failed pass is
/// recorded in <see cref="TripWorkerState"/> for <c>/health</c>, logged, and retried;
/// telemetry ingest and the REST API share this process and matter more than trips being
/// a minute late. A scope per pass, so the pass's <c>DbContext</c> never outlives it.
/// </para>
/// </summary>
internal sealed class TripDetectionWorker : BackgroundService
{
    /// <summary>First retry delay after a failed pass.</summary>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>Longest retry delay; the backoff doubles up to here.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITripDetectionSignal _signal;
    private readonly TripWorkerState _state;
    private readonly ILogger<TripDetectionWorker> _logger;

    /// <summary>Creates the worker.</summary>
    /// <param name="scopeFactory">Opens a DI scope per pass.</param>
    /// <param name="signal">Raised by ingest after each save.</param>
    /// <param name="state">Records each pass's outcome for the health endpoint.</param>
    /// <param name="logger">Structured logger — counts only, never coordinates.</param>
    public TripDetectionWorker(
        IServiceScopeFactory scopeFactory,
        ITripDetectionSignal signal,
        TripWorkerState state,
        ILogger<TripDetectionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _state = state;
        _logger = logger;
    }

    /// <summary>Runs the startup pass, then one pass per wake-up until the host stops.</summary>
    /// <param name="stoppingToken">Signalled on shutdown.</param>
    /// <returns>A task that completes when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let host startup finish before the first (possibly long) backfill pass begins.
        await Task.Yield();

        _logger.LogInformation("Trip detection worker started; passes run after each position save");

        bool runNow = true;
        TimeSpan? retryDelay = null;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!runNow)
                {
                    await WaitForWorkAsync(retryDelay, stoppingToken);
                }

                TripPassOutcome? outcome = await RunPassAsync(stoppingToken);
                if (outcome is null)
                {
                    retryDelay = retryDelay is null
                        ? FirstRetryDelay
                        : TimeSpan.FromTicks(Math.Min(retryDelay.Value.Ticks * 2, MaxRetryDelay.Ticks));
                    runNow = false;
                }
                else
                {
                    retryDelay = null;
                    runNow = outcome.MoreWork;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown. The next start's catch-up pass picks up anything missed.
        }

        _logger.LogInformation("Trip detection worker stopped");
    }

    // Waits for a save — or, after a failure, for whichever comes first: a save or the
    // retry delay. A cancelled wait consumes no notification.
    private async Task WaitForWorkAsync(TimeSpan? retryDelay, CancellationToken stoppingToken)
    {
        if (retryDelay is null)
        {
            await _signal.WaitAsync(stoppingToken);
            return;
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(retryDelay.Value);

        try
        {
            await _signal.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The retry delay elapsed with no save in between: retry anyway.
        }
    }

    // One pass in its own scope; a failure becomes a record and a log line, never an
    // exception on a background thread. Null means the pass failed.
    private async Task<TripPassOutcome?> RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ITripDetectionPass pass = scope.ServiceProvider.GetRequiredService<ITripDetectionPass>();

            DateTime utcNow = DateTime.UtcNow;
            TripPassOutcome outcome = await pass.RunAsync(utcNow, cancellationToken);
            _state.RecordPassSucceeded(outcome.TripsChanged, utcNow);

            // Quiet unless something changed; a pass per save would otherwise turn the log
            // into a metronome.
            if (outcome.TripsChanged > 0)
            {
                _logger.LogInformation(
                    "Trip pass recomputed {DeviceCount} device(s), {TripsChanged} trip row(s) changed{MoreWork}",
                    outcome.DevicesProcessed,
                    outcome.TripsChanged,
                    outcome.MoreWork ? "; continuing" : string.Empty);
            }

            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _state.RecordPassFailed();
            _logger.LogWarning(exception, "Trip pass failed; trips may lag behind positions until a later pass succeeds");
            return null;
        }
    }
}
