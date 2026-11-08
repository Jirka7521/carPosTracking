using System.Threading.Channels;

namespace CarPosAPI.Services.Trips;

/// <summary>
/// Implements <see cref="ITripDetectionSignal"/> with a one-slot channel that drops
/// further writes while full.
///
/// <para>
/// One slot is exactly the semantics wanted: "at least one save happened since the
/// worker last looked". A burst of a hundred saves from a device draining its SD-card
/// backlog becomes one pending notification, and the pass that answers it reads all of
/// them at once. A notification arriving <em>during</em> a pass fills the slot again,
/// so nothing that landed after the pass's read is left waiting.
/// </para>
///
/// Singleton: ingest (a singleton) writes, the hosted worker reads.
/// </summary>
internal sealed class TripDetectionSignal : ITripDetectionSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <inheritdoc />
    public void Notify()
    {
        // TryWrite on a DropWrite channel never blocks; when the slot is taken the
        // notification is already pending, which is all it needed to say.
        _channel.Writer.TryWrite(true);
    }

    /// <inheritdoc />
    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        await _channel.Reader.ReadAsync(cancellationToken);
    }
}
