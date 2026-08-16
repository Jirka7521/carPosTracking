using System.Threading;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Thread-safe snapshot of the ingest connection and its counters, shared
/// between the MQTT service (writer) and the health check (reader). Interlocked
/// primitives instead of locks: writers are on the hot message path, readers are
/// occasional health probes.
/// </summary>
internal sealed class MqttConnectionState
{
    private long _isConnected;
    private long _messagesReceived;
    private long _positionsInserted;
    private long _positionsDuplicate;
    private long _envelopesRejected;
    private long _lastMessageAtUtcTicks;

    /// <summary>Whether the MQTT client currently holds a broker connection.</summary>
    public bool IsConnected => Interlocked.Read(ref _isConnected) == 1;

    /// <summary>Messages received since startup.</summary>
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);

    /// <summary>Position rows inserted since startup.</summary>
    public long PositionsInserted => Interlocked.Read(ref _positionsInserted);

    /// <summary>Duplicate fixes skipped since startup.</summary>
    public long PositionsDuplicate => Interlocked.Read(ref _positionsDuplicate);

    /// <summary>Envelopes rejected (structural, crypto or validation) since startup.</summary>
    public long EnvelopesRejected => Interlocked.Read(ref _envelopesRejected);

    /// <summary>UTC time of the last received message, or null before the first one.</summary>
    public DateTime? LastMessageAtUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastMessageAtUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Records a connect (true) or disconnect (false).</summary>
    /// <param name="connected">The new connection state.</param>
    public void SetConnected(bool connected)
    {
        Interlocked.Exchange(ref _isConnected, connected ? 1 : 0);
    }

    /// <summary>Records one received message.</summary>
    public void RecordMessage()
    {
        Interlocked.Increment(ref _messagesReceived);
        Interlocked.Exchange(ref _lastMessageAtUtcTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>Adds one message's processing outcome to the counters.</summary>
    /// <param name="inserted">Rows inserted.</param>
    /// <param name="duplicates">Fixes skipped as duplicates.</param>
    /// <param name="rejected">Envelopes rejected.</param>
    public void RecordOutcome(int inserted, int duplicates, int rejected)
    {
        Interlocked.Add(ref _positionsInserted, inserted);
        Interlocked.Add(ref _positionsDuplicate, duplicates);
        Interlocked.Add(ref _envelopesRejected, rejected);
    }
}
