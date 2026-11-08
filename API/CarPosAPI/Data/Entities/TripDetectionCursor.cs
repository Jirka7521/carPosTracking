namespace CarPosAPI.Data.Entities;

/// <summary>
/// The single-row watermark through which the trip detection worker finds new
/// positions: every <see cref="Position"/> with an id above
/// <see cref="LastPositionId"/> has not been looked at yet.
///
/// <para>
/// Safe because positions are written by exactly one process, one batch at a time
/// (<c>Services.Ingest.PositionWriter</c>), so identity values commit in order and a
/// scan by id can never skip a row. Starting at 0 makes the first pass backfill the
/// whole history.
/// </para>
/// Mapped by <see cref="Configurations.TripDetectionCursorConfiguration"/>.
/// </summary>
public sealed class TripDetectionCursor
{
    /// <summary>The only row's key, always <see cref="SingletonId"/>.</summary>
    public short Id { get; set; }

    /// <summary>Highest position id already folded into some device's dirty range.</summary>
    public long LastPositionId { get; set; }

    /// <summary>The fixed primary key of the one row (CHECK-enforced).</summary>
    public const short SingletonId = 1;
}
