using CarPosAPI.Services.Trips;

namespace CarPosAPI.Tests;

/// <summary>
/// Covers <see cref="TripDetectionSignal"/> — the wake-up call ingest raises after every
/// save. It sits on the ingest path, so the properties that matter are that notifying
/// never blocks, that a burst merges into one pending wake-up, and that a wait abandoned
/// by cancellation leaves the notification for the next one.
/// </summary>
public sealed class TripDetectionSignalTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task ABurstOfSaves_WakesTheWorkerOnce()
    {
        TripDetectionSignal signal = new TripDetectionSignal();

        // An SD-card backlog drain: many saves in quick succession, nobody reading.
        for (int save = 0; save < 1_000; save++)
        {
            signal.Notify();
        }

        await signal.WaitAsync(CancellationToken.None);

        using CancellationTokenSource timeout = new CancellationTokenSource(Soon);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await signal.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task ASaveDuringAPass_WakesTheWorkerAgain()
    {
        TripDetectionSignal signal = new TripDetectionSignal();
        signal.Notify();
        await signal.WaitAsync(CancellationToken.None);

        // The pass is running; a new save lands.
        signal.Notify();

        Task wait = signal.WaitAsync(CancellationToken.None).AsTask();
        Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(Soon)));
    }

    [Fact]
    public async Task ACancelledWait_DoesNotConsumeTheNotification()
    {
        TripDetectionSignal signal = new TripDetectionSignal();

        using (CancellationTokenSource timeout = new CancellationTokenSource(Soon))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await signal.WaitAsync(timeout.Token));
        }

        signal.Notify();
        Task wait = signal.WaitAsync(CancellationToken.None).AsTask();
        Assert.Same(wait, await Task.WhenAny(wait, Task.Delay(Soon)));
    }
}
