using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using CarPosAPI.Services.Ingest;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Scheduling;

/// <summary>
/// Implements <see cref="IScheduleBundlePublisher"/>.
/// </summary>
internal sealed class ScheduleBundlePublisher : IScheduleBundlePublisher
{
    private readonly CarPosDbContext _context;
    private readonly ScheduleBundleBuilder _builder;
    private readonly IConfigPublisher _publisher;
    private readonly ILogger<ScheduleBundlePublisher> _logger;

    /// <summary>Creates the publisher.</summary>
    /// <param name="context">Scoped database context, shared with the caller.</param>
    /// <param name="builder">Assembles the bundle from the rows.</param>
    /// <param name="publisher">Puts it on the broker, retained.</param>
    /// <param name="logger">Structured logger.</param>
    public ScheduleBundlePublisher(
        CarPosDbContext context,
        ScheduleBundleBuilder builder,
        IConfigPublisher publisher,
        ILogger<ScheduleBundlePublisher> logger)
    {
        _context = context;
        _builder = builder;
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> PublishAsync(Device device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Bumped unconditionally, even when the rebuilt bundle turns out identical.
        // The number is not a content hash and is not trying to be one: its job is to
        // let the device say "I have seen everything up to revision N", and a change
        // that did not move the number would leave a device that missed it looking
        // perfectly in step.
        device.ScheduleBundleVersion++;
        await _context.SaveChangesAsync(cancellationToken);

        // Built after the save, so the bundle carries the revision that is now
        // committed rather than one a rollback could have erased — the same ordering
        // DeviceConfigRevisionWriter uses for the config document, and for the same
        // reason.
        DeviceScheduleBundleDto? bundle =
            await _builder.BuildAsync(_context, device.Id, cancellationToken);

        if (bundle is null)
        {
            // Unreachable through the service, which has already loaded this row.
            _logger.LogWarning(
                "Device {DeviceId}: schedule bundle could not be built; the device keeps the one it has",
                device.DeviceId);
            return device.ScheduleBundleVersion;
        }

        await _publisher.PublishScheduleAsync(device.DeviceId, bundle, cancellationToken);
        return bundle.ScheduleVersion;
    }
}
