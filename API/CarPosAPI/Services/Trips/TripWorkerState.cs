using System.Threading;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Thread-safe snapshot of the trip detection worker's passes, shared between
/// <see cref="TripDetectionWorker"/> (writer) and
/// <see cref="Health.TripWorkerHealthCheck"/> (reader) — the sibling of
/// <c>ScheduleWorkerState</c>, built the same way for the same reasons.
///
/// <para>
/// The worker swallows every exception so a database blip costs one pass, not the
/// process. Without this state a broken worker would be invisible: trips would simply
/// stop appearing while the API looked healthy.
/// </para>
/// </summary>
internal sealed class TripWorkerState
{
    private long _passesCompleted;
    private long _passesFailed;
    private long _consecutiveFailures;
    private long _tripsChangedTotal;
    private long _lastSuccessfulPassAtUtcTicks;

    /// <summary>Passes that finished without throwing, since startup.</summary>
    public long PassesCompleted => Interlocked.Read(ref _passesCompleted);

    /// <summary>Passes that failed, since startup.</summary>
    public long PassesFailed => Interlocked.Read(ref _passesFailed);

    /// <summary>Failures since the last success.</summary>
    public long ConsecutiveFailures => Interlocked.Read(ref _consecutiveFailures);

    /// <summary>Trip rows inserted, moved or deleted by passes, summed since startup.</summary>
    public long TripsChangedTotal => Interlocked.Read(ref _tripsChangedTotal);

    /// <summary>When the last successful pass ran (UTC), or null before the first.</summary>
    public DateTime? LastSuccessfulPassAtUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastSuccessfulPassAtUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Records a pass that completed, and clears the failure streak.</summary>
    /// <param name="tripsChanged">Trip rows that pass inserted, moved or deleted.</param>
    /// <param name="passAtUtc">When the pass ran.</param>
    public void RecordPassSucceeded(int tripsChanged, DateTime passAtUtc)
    {
        Interlocked.Increment(ref _passesCompleted);
        Interlocked.Add(ref _tripsChangedTotal, tripsChanged);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _lastSuccessfulPassAtUtcTicks, passAtUtc.Ticks);
    }

    /// <summary>Records a pass that threw.</summary>
    public void RecordPassFailed()
    {
        Interlocked.Increment(ref _passesFailed);
        Interlocked.Increment(ref _consecutiveFailures);
    }
}
