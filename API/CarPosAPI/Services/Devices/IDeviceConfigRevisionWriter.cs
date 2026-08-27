using CarPosAPI.Data.Entities;
using CarPosAPI.Dtos;

namespace CarPosAPI.Services.Devices;

/// <summary>
/// The one writer of <see cref="DeviceConfigVersion"/> rows: appends a revision, moves
/// the device's pointer to it, and publishes the document to the broker retained.
///
/// <para>
/// Extracted from <see cref="DeviceConfigService"/> when schedules arrived, because
/// there are now two callers with nothing else in common — a request that has a user
/// and an authorization check, and a background pass that has neither. Everything
/// subtle about the write lives here rather than in both: the "saving what is already
/// in force appends nothing" rule, the single transaction over the row and the pointer,
/// and publishing strictly after the commit.
/// </para>
///
/// <para>
/// <b>It authorizes nothing.</b> Callers must have established that the operation is
/// allowed before they get here; this is the layer below that decision, and the
/// scheduler legitimately has no caller to check.
/// </para>
///
/// <para>
/// Scoped, sharing the caller's <c>CarPosDbContext</c>. That is deliberate and load
/// bearing: a caller may stage other changes on the same device — stamping an override,
/// say — and they are committed in this method's transaction, so a device can never end
/// up with a new revision but no override, or the reverse.
/// </para>
/// </summary>
internal interface IDeviceConfigRevisionWriter
{
    /// <summary>
    /// Makes <paramref name="values"/> the device's settings, appending a revision and
    /// publishing it — unless they are already in force, in which case nothing is
    /// appended and nothing is published.
    /// </summary>
    /// <param name="deviceRowId">Internal device id. The caller has already authorised this.</param>
    /// <param name="values">The complete new settings — a replacement, never a patch.</param>
    /// <param name="authorUserId">Who to record as the author, or null for the scheduler.</param>
    /// <param name="source">Whether a person or the scheduler produced this.</param>
    /// <param name="sourceProfileId">The profile the values came from, for a scheduled write.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The revision now in force and whether it is a new one, or null when the device
    /// row has vanished between the caller's check and this write.
    /// </returns>
    Task<ConfigRevisionOutcome?> ApplyAsync(
        Guid deviceRowId,
        DeviceConfigValuesDto values,
        int? authorUserId,
        ConfigRevisionSource source,
        Guid? sourceProfileId,
        CancellationToken cancellationToken);
}
