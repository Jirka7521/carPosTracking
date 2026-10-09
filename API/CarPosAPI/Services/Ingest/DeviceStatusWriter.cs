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
/// within that boot — each one a real, separate connection loss. The only true
/// duplicate is a QoS 2 redelivery after this process died between commit and PUBREC,
/// which is rare and costs one repeated row in a history list.
/// </para>
///
/// <para>
/// The event's time and the device's online time come from one server-side
/// <see cref="DateTime.UtcNow"/>, so an online message that also records a restart
/// stamps both with the same instant, and device clocks never enter the ordering.
/// </para>
/// </summary>
internal sealed class DeviceStatusWriter : IDeviceStatusWriter
{
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
}
