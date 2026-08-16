using CarPosAPI.Data;
using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CarPosAPI.Services.Authorization;

/// <summary>
/// Resolves a caller's grant on a device with one indexed join.
///
/// The join is on the <em>active</em> grant only, which is what makes revocation
/// instant: <c>DELETE /api/access/{id}</c> flips <c>is_active</c> and the very
/// next request from that user stops resolving. Soft-deleted devices still
/// resolve — their positions remain readable, and someone has to be able to see
/// the row in order to know it was deactivated — so callers that perform writes
/// check <see cref="DeviceAccessContext.IsActive"/> themselves.
///
/// Scoped, alongside the <see cref="CarPosDbContext"/> it uses.
/// </summary>
internal sealed class DeviceAccessAuthorizer : IDeviceAccessAuthorizer
{
    private readonly CarPosDbContext _context;

    /// <summary>Creates the authorizer.</summary>
    /// <param name="context">Scoped database context.</param>
    public DeviceAccessAuthorizer(CarPosDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<DeviceAccessContext?> ResolveAsync(
        int userId,
        string deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        // One round trip, projected in SQL: the caller gets exactly the four flags
        // and two ids, and no device column that has no business leaving the
        // database (the private key ciphertext, above all) is ever materialised.
        return await _context.Accesses
            .AsNoTracking()
            .Where(access => access.UserId == userId && access.IsActive)
            .Join(
                _context.Devices.AsNoTracking().Where(device => device.DeviceId == deviceId),
                access => access.DeviceId,
                device => device.Id,
                (Access access, Device device) => new DeviceAccessContext(
                    device.Id,
                    device.DeviceId,
                    device.IsActive,
                    new DevicePermissionsDto(
                        access.CanRead,
                        access.CanDelete,
                        access.CanShare,
                        access.CanModifySettings)))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
