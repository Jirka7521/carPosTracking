using System.Threading;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Thread-safe snapshot of the schedule worker's passes, shared between
/// <see cref="DeviceConfigScheduleWorker"/> (writer) and the health check
/// (reader).
///
/// <para>
/// The worker swallows every exception on purpose — a database blip must cost one
/// pass, not the process — which leaves a failure mode with no outward sign: the
/// fleet quietly runs stale settings while the API looks perfectly well. Nothing
/// but a log line said so. This is the state a probe can read.
/// </para>
///
/// <para>
/// Interlocked primitives rather than locks, and a ticks field rather than a
/// <c>DateTime</c>, for the same reasons as
/// <see cref="Ingest.MqttConnectionState"/>: one writer on a timer, occasional
/// readers, and a 64-bit field that must not be read half-written on a 32-bit
/// runtime.
/// </para>
///
/// <para>
/// The pass instant is passed in rather than read from the clock here, following
/// <see cref="IScheduleReconciler.ReconcileAsync"/> — the time the fleet was
/// evaluated against is the time worth reporting, and a class that takes its own
/// clock reading cannot be tested against one.
/// </para>
/// </summary>
internal sealed class ScheduleWorkerState
{
    private long _passesCompleted;
    private long _passesFailed;
    private long _consecutiveFailures;
    private long _devicesChangedTotal;
    private long _lastSuccessfulPassAtUtcTicks;

    /// <summary>Passes that finished without throwing, since startup.</summary>
    public long PassesCompleted => Interlocked.Read(ref _passesCompleted);

    /// <summary>Passes that failed, since startup.</summary>
    public long PassesFailed => Interlocked.Read(ref _passesFailed);

    /// <summary>
    /// Failures since the last success. This, not the total, is what separates a
    /// transient blip from a worker that has been broken all afternoon.
    /// </summary>
    public long ConsecutiveFailures => Interlocked.Read(ref _consecutiveFailures);

    /// <summary>Devices whose settings a pass changed, summed since startup.</summary>
    public long DevicesChangedTotal => Interlocked.Read(ref _devicesChangedTotal);

    /// <summary>
    /// The instant the last successful pass evaluated the fleet against (UTC), or
    /// null when none has yet — the normal state for the first moments after
    /// startup.
    /// </summary>
    public DateTime? LastSuccessfulPassAtUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastSuccessfulPassAtUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Records a pass that completed, and clears the failure streak.</summary>
    /// <param name="devicesChanged">How many devices that pass reconciled.</param>
    /// <param name="passAtUtc">The instant the pass evaluated the fleet against.</param>
    public void RecordPassSucceeded(int devicesChanged, DateTime passAtUtc)
    {
        Interlocked.Increment(ref _passesCompleted);
        Interlocked.Add(ref _devicesChangedTotal, devicesChanged);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _lastSuccessfulPassAtUtcTicks, passAtUtc.Ticks);
    }

    /// <summary>
    /// Records a pass that threw. The last-success timestamp is deliberately left
    /// alone: it is what a reader uses to say how long the fleet has been running
    /// on settings nobody has checked.
    /// </summary>
    public void RecordPassFailed()
    {
        Interlocked.Increment(ref _passesFailed);
        Interlocked.Increment(ref _consecutiveFailures);
    }
}
