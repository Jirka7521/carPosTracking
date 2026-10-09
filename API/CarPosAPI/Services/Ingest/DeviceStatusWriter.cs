using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarPosAPI.Services.Ingest;

/// <summary>
/// Implements <see cref="IDeviceStatusWriter"/> with one short transaction per message.
///
/// <para>
/// <b>No deduplication, deliberately.</b> Positions dedupe on (device, fix time), but a
/// status message has no natural identity: the Last Will is sealed once per boot and
/// the broker legitimately publishes the very same bytes again after every reconnect
/// within that boot — each one a real, separate connection loss. The true duplicates
/// are a QoS 2 redelivery after this process died between commit and PUBREC, and a
/// burst the device re-sends from its card because the broker's ack never reached it;
/// both are rare and cost a repeated row in a history list.
/// </para>
///
/// <para>
/// The receive time and the device's online time come from one server-side
/// <see cref="DateTime.UtcNow"/>, so an online message that also records a restart
/// stamps both with the same instant. The event's own time is that same instant
/// unless the message plainly waited on the device's card — see
/// <see cref="ResolveOccurredAt"/>.
/// </para>
/// </summary>
internal sealed class DeviceStatusWriter : IDeviceStatusWriter
{
    /// <summary>
    /// How far a device's clock must lag the receive time before the message counts as
    /// replayed from the card. A live message reaches the API within seconds; the
    /// device's clock coasts on an RC oscillator between GNSS fixes and drifts by about
    /// a minute at worst. Two minutes clears both, and anything a card held for longer
    /// is placed by the device's clock instead of by its arrival.
    /// </summary>
    internal static readonly TimeSpan ReplayThreshold = TimeSpan.FromMinutes(2);

    private readonly IDbContextFactory<CarPosDbContext> _contextFactory;

    /// <summary>Creates the writer.</summary>
    /// <param name="contextFactory">Factory for short-lived DbContexts (singleton-safe).</param>
    public DeviceStatusWriter(IDbContextFactory<CarPosDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        Guid deviceRowId,
        ValidatedDeviceStatus status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (status.Event is null && !status.IsOnline)
        {
            return;  // nothing to record (cannot happen today: every offline classifies)
        }

        DateTime receivedAt = DateTime.UtcNow;

        await using CarPosDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // The connection retries transient faults (Program.cs), and EF will only run a
        // hand-opened transaction inside its execution strategy, which may replay it.
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            async (CancellationToken attemptToken) =>
            {
                // A failed attempt leaves its row staged as Added; without this a replay
                // would insert it twice.
                context.ChangeTracker.Clear();

                await using IDbContextTransaction transaction =
                    await context.Database.BeginTransactionAsync(attemptToken);

                if (status.Event is not null)
                {
                    context.DeviceEvents.Add(new DeviceEvent
                    {
                        DeviceId = deviceRowId,
                        ReceivedAt = receivedAt,
                        DeviceTime = status.DeviceTimeUtc,
                        OccurredAt = ResolveOccurredAt(receivedAt, status.DeviceTimeUtc),
                        Kind = status.Event.Kind,
                        Reason = status.Event.Reason,
                        Severity = status.Event.Severity,
                        BatteryPct = status.BatteryPct,
                        SleepSeconds = status.SleepSeconds,
                        Detail = status.Detail,
                    });
                    await context.SaveChangesAsync(attemptToken);
                }

                if (status.IsOnline)
                {
                    await context.Devices
                        .Where(device => device.Id == deviceRowId)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(device => device.LastOnlineAt, _ => receivedAt),
                            attemptToken);
                }

                await transaction.CommitAsync(attemptToken);
            },
            cancellationToken);
    }

    /// <summary>
    /// When an event happened. The receive time, which every other timestamp in the
    /// system shares — unless the device's own clock puts it more than
    /// <see cref="ReplayThreshold"/> earlier, which only a message that waited on the
    /// device's SD card does. Never later than the receive time: a device clock running
    /// fast must not move an event into the future.
    /// </summary>
    /// <param name="receivedAt">Server receive time (UTC).</param>
    /// <param name="deviceTime">The device's clock at the time, or null when it sent none.</param>
    /// <returns>The event's time (UTC).</returns>
    internal static DateTime ResolveOccurredAt(DateTime receivedAt, DateTime? deviceTime)
    {
        if (deviceTime is DateTime said && receivedAt - said > ReplayThreshold)
        {
            return said;
        }

        return receivedAt;
    }
}
